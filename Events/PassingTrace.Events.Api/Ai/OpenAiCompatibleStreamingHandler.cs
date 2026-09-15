using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace PassingTrace.Events.Api.Ai;

/// <summary>
/// Adapts known protocol omissions made by some OpenAI-compatible providers:
/// unfinished chunks may use an empty finish_reason and continuation tool-call
/// fragments may omit their already-established function kind. It never invents
/// a successful terminal reason or rewrites an unknown nonempty value.
/// </summary>
public sealed class OpenAiCompatibleStreamingHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode &&
            request.RequestUri?.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal) == true &&
            string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            response.Content = new CompatibleStreamingContent(response.Content);
        }
        return response;
    }

    private sealed class CompatibleStreamingContent : HttpContent
    {
        private readonly HttpContent _inner;

        public CompatibleStreamingContent(HttpContent inner)
        {
            _inner = inner;
            foreach (var header in inner.Headers)
            {
                if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override bool TryComputeLength(out long length) { length = 0; return false; }

        protected override Task<Stream> CreateContentReadStreamAsync() => CreateContentReadStreamAsync(CancellationToken.None);

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            new CompatibleSseStream(await _inner.ReadAsStreamAsync(cancellationToken));

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            using var adapted = await CreateContentReadStreamAsync();
            await adapted.CopyToAsync(stream);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class CompatibleSseStream(Stream inner) : Stream
    {
        private readonly StreamReader _reader = new(inner, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        private readonly HashSet<int> _seenChoices = [];
        private readonly HashSet<int> _finishedChoices = [];
        private readonly HashSet<(int Choice, int Tool)> _functionToolCalls = [];
        private byte[] _pending = [];
        private int _position;
        private bool _ended;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty) return 0;
            if (_position == _pending.Length)
            {
                if (_ended) return 0;
                _pending = await ReadFrameAsync(cancellationToken);
                _position = 0;
                if (_pending.Length == 0) return 0;
            }
            var count = Math.Min(buffer.Length, _pending.Length - _position);
            _pending.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        private async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
        {
            // Buffer only one SSE event, never the complete answer.
            var lines = new List<string>();
            var data = new List<string>();
            var size = 0;
            while (true)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    if (lines.Count != 0) throw new IncompleteAiResponseException();
                    ValidateFinished();
                    _ended = true;
                    return [];
                }
                size += line.Length;
                if (size > 1024 * 1024) throw new IncompleteAiResponseException();
                if (line.Length == 0) break;
                lines.Add(line);
                if (line.StartsWith("data:", StringComparison.Ordinal))
                    data.Add(line[5..].TrimStart(' '));
            }

            if (data.Count != 0)
            {
                var payload = string.Join('\n', data);
                if (payload == "[DONE]")
                {
                    ValidateFinished();
                    _ended = true;
                }
                else
                {
                    var adapted = Normalize(payload);
                    if (adapted is not null)
                    {
                        lines.RemoveAll(line => line.StartsWith("data:", StringComparison.Ordinal));
                        lines.Add("data: " + adapted);
                    }
                }
            }
            return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n\n");
        }

        private string? Normalize(string payload)
        {
            var root = JsonNode.Parse(payload);
            if (root?["choices"] is not JsonArray choices) return null;
            var changed = false;
            foreach (var choice in choices.OfType<JsonObject>())
            {
                var index = choice["index"]?.GetValue<int>() ?? 0;
                _seenChoices.Add(index);

                if (choice["delta"]?["tool_calls"] is JsonArray toolCalls)
                {
                    foreach (var toolCall in toolCalls.OfType<JsonObject>())
                    {
                        var toolIndex = toolCall["index"]?.GetValue<int>() ?? 0;
                        var key = (index, toolIndex);
                        var hasFunctionPayload = toolCall["function"] is JsonObject;
                        string? toolType = null;
                        var hasStringType = toolCall["type"] is JsonValue typeValue &&
                            typeValue.TryGetValue<string>(out toolType);

                        if (hasStringType && string.Equals(toolType, "function", StringComparison.Ordinal))
                        {
                            _functionToolCalls.Add(key);
                        }
                        else if ((!hasStringType || string.IsNullOrEmpty(toolType)) &&
                                 (hasFunctionPayload || _functionToolCalls.Contains(key)))
                        {
                            toolCall["type"] = "function";
                            _functionToolCalls.Add(key);
                            changed = true;
                        }
                        // Unknown nonempty tool kinds stay untouched so the SDK rejects them.
                    }
                }

                if (choice["finish_reason"] is JsonValue value && value.TryGetValue<string>(out var reason))
                {
                    if (reason == string.Empty)
                    {
                        choice["finish_reason"] = null;
                        changed = true;
                    }
                    else if (reason is "stop" or "tool_calls" or "function_call" or "length" or "content_filter")
                    {
                        _finishedChoices.Add(index);
                    }
                    // Unknown nonempty finish reasons stay untouched so the SDK rejects them.
                }
            }
            return changed ? root!.ToJsonString() : null;
        }

        private void ValidateFinished()
        {
            if (_seenChoices.Count == 0 || !_seenChoices.IsSubsetOf(_finishedChoices))
                throw new IncompleteAiResponseException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _reader.Dispose();
            base.Dispose(disposing);
        }
    }
}

public sealed class IncompleteAiResponseException() : Exception("AI stream ended before a terminal response was received.");
