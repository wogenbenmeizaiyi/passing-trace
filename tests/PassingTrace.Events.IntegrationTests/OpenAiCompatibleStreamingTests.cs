using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using PassingTrace.Events.Api.Ai;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class OpenAiCompatibleStreamingTests
{
    [Fact]
    public async Task OfficialSdkWithoutAdapter_ReproducesEmptyFinishReasonFailure()
    {
        using var http = new HttpClient(new StubHandler(StreamOf(Chunk("hello", ""), Chunk("", "stop"))));
        using var client = CreateClient(http);

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ReadAllAsync(client));

        Assert.Contains("Unknown ChatFinishReason", error.Message);
    }

    [Fact]
    public async Task OfficialSdkWithoutAdapter_ReproducesEmptyToolCallKindFailure()
    {
        var stream = StreamOf(
            ToolChunk("{\"metric\":", true, "function", null),
            ToolChunk("\"count\"}", false, "", null),
            Chunk("", "tool_calls"));
        using var http = new HttpClient(new StubHandler(stream));
        using var client = CreateClient(http);

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ReadAllAsync(client));

        Assert.Contains("Unknown ChatToolCallKind", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ContinuationFinishReason_ReachesOfficialSdkAsUnfinished(string? reason)
    {
        using var http = AdaptedHttp(StreamOf(Chunk("你好", reason), Chunk("！", reason), Chunk("", "stop")));
        using var client = CreateClient(http);

        var updates = await ReadAllAsync(client);

        Assert.Equal("你好！", string.Concat(updates.Select(x => x.Text)));
        Assert.Equal(ChatFinishReason.Stop, updates.Last(x => x.FinishReason.HasValue).FinishReason);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("length")]
    [InlineData("content_filter")]
    public async Task TerminalReason_IsNotRewrittenToSuccess(string reason)
    {
        using var http = AdaptedHttp(StreamOf(Chunk("partial", ""), Chunk("", reason)));
        using var client = CreateClient(http);

        var updates = await ReadAllAsync(client);

        Assert.Equal(new ChatFinishReason(reason), updates.Last(x => x.FinishReason.HasValue).FinishReason);
    }

    [Fact]
    public async Task ToolCallFragments_WithEmptyContinuationKind_ExecuteExactlyOnce()
    {
        var calls = 0;
        var first = StreamOf(
            ToolChunk("{\"metric\":", true, "function"),
            ToolChunk("\"count\"}", false, ""),
            Chunk("", "tool_calls"));
        var second = StreamOf(Chunk("共有三条记录", ""), Chunk("", "stop"));
        var handler = new StubHandler(first, second);
        using var http = new HttpClient(new OpenAiCompatibleStreamingHandler(handler));
        using var client = CreateClient(http).AsBuilder().UseFunctionInvocation().Build();
        var function = AIFunctionFactory.Create((string metric) => { calls++; Assert.Equal("count", metric); return 3; }, "CountRecords");

        var updates = await ReadAllAsync(client, new ChatOptions { Tools = [function] });

        Assert.Equal(1, calls);
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains("共有三条记录", string.Concat(updates.Select(x => x.Text)));
    }

    [Fact]
    public async Task FirstToolCallFragment_WithEmptyKind_IsRecoveredFromFunctionPayload()
    {
        var calls = 0;
        var first = StreamOf(
            ToolChunk("{\"metric\":\"count\"}", true, ""),
            Chunk("", "tool_calls"));
        var second = StreamOf(Chunk("共有三条记录", ""), Chunk("", "stop"));
        using var http = new HttpClient(new OpenAiCompatibleStreamingHandler(new StubHandler(first, second)));
        using var client = CreateClient(http).AsBuilder().UseFunctionInvocation().Build();
        var function = AIFunctionFactory.Create((string metric) => { calls++; return metric == "count" ? 3 : 0; }, "CountRecords");

        var updates = await ReadAllAsync(client, new ChatOptions { Tools = [function] });

        Assert.Equal(1, calls);
        Assert.Contains("共有三条记录", string.Concat(updates.Select(x => x.Text)));
    }

    [Fact]
    public async Task UnknownNonemptyToolCallKind_IsNotSilentlyAccepted()
    {
        using var http = AdaptedHttp(StreamOf(ToolChunk("{}", true, "provider_tool"), Chunk("", "tool_calls")));
        using var client = CreateClient(http);

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ReadAllAsync(client));

        Assert.Contains("Unknown ChatToolCallKind", error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StreamWithoutTerminalReason_IsRejectedEvenWithDoneMarker(bool done)
    {
        var body = StreamOf(Chunk("partial", ""), done: done);
        using var http = AdaptedHttp(body);
        using var client = CreateClient(http);

        await Assert.ThrowsAsync<IncompleteAiResponseException>(() => ReadAllAsync(client));
    }

    [Fact]
    public async Task UnknownNonemptyReason_IsNotSilentlyAccepted()
    {
        using var http = AdaptedHttp(StreamOf(Chunk("", "provider_failure")));
        using var client = CreateClient(http);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ReadAllAsync(client));
    }

    [Fact]
    public async Task UsageOnlyFinalChunk_IsPreservedAfterTerminalChoice()
    {
        var usage = "{\"id\":\"test\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"test\",\"choices\":[],\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":3,\"total_tokens\":5}}";
        using var http = AdaptedHttp(StreamOf(Chunk("hello", ""), Chunk("", "stop"), usage));
        using var client = CreateClient(http);

        var updates = await ReadAllAsync(client);

        var content = updates.SelectMany(x => x.Contents).OfType<UsageContent>().Single();
        Assert.Equal(5, content.Details.TotalTokenCount);
    }

    [Fact]
    public async Task NonStreamingJsonResponse_IsUnchanged()
    {
        const string json = "{\"id\":\"test\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"test\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"hello\"},\"finish_reason\":\"stop\"}]}";
        using var http = new HttpClient(new OpenAiCompatibleStreamingHandler(new StubHandler(json) { ContentType = "application/json" }));
        using var client = CreateClient(http);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal("hello", response.Text);
    }

    [Fact]
    public async Task FirstDeltaIsDeliveredBeforeRemainingNetworkStreamArrives()
    {
        using var stream = new GatedStream(Encoding.UTF8.GetBytes("data: " + Chunk("first", "") + "\n\n"),
            Encoding.UTF8.GetBytes(StreamOf(Chunk("", "stop"))));
        using var http = new HttpClient(new OpenAiCompatibleStreamingHandler(new StreamHandler(stream)));
        using var client = CreateClient(http);
        await using var enumerator = client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("first", enumerator.Current.Text);
        stream.Release();
        while (await enumerator.MoveNextAsync()) { }
    }

    private static IChatClient CreateClient(HttpClient http) => new OpenAIClient(new ApiKeyCredential("test-only"),
        new OpenAIClientOptions { Endpoint = new Uri("https://mock.invalid/v1/"), Transport = new HttpClientPipelineTransport(http) })
        .GetChatClient("test-model").AsIChatClient();

    private static HttpClient AdaptedHttp(string body) => new(new OpenAiCompatibleStreamingHandler(new StubHandler(body)));

    private static async Task<List<ChatResponseUpdate>> ReadAllAsync(IChatClient client, ChatOptions? options = null)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test question")], options)) updates.Add(update);
        return updates;
    }

    private static string Chunk(string content, string? reason) => JsonSerializer.Serialize(new
    {
        id = "test",
        @object = "chat.completion.chunk",
        created = 1,
        model = "test-model",
        choices = new[] { new { index = 0, delta = new { role = "assistant", content }, finish_reason = reason } },
    });

    private static string ToolChunk(string arguments, bool first, string? type, string? finishReason = "") => JsonSerializer.Serialize(new
    {
        id = "test",
        @object = "chat.completion.chunk",
        created = 1,
        model = "test-model",
        choices = new[] { new { index = 0, delta = new { role = "assistant", tool_calls = new[] { new { index = 0,
            id = first ? "call_1" : null, type, function = new { name = first ? "CountRecords" : null, arguments } } } }, finish_reason = finishReason } },
    });

    private static string StreamOf(params string[] chunks) => StreamOf(chunks, true);
    private static string StreamOf(string chunk, bool done) => StreamOf([chunk], done);
    private static string StreamOf(string[] chunks, bool done) => string.Concat(chunks.Select(x => "data: " + x + "\r\n\r\n")) + (done ? "data: [DONE]\r\n\r\n" : "");

    private sealed class StubHandler(params string[] responses) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string ContentType { get; init; } = "text/event-stream";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses[RequestCount++], Encoding.UTF8, ContentType) });
    }

    private sealed class StreamHandler(Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(stream);
            content.Headers.ContentType = new("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class GatedStream(byte[] first, byte[] rest) : Stream
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;
        public void Release() => _gate.TrySetResult();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < first.Length)
            {
                var count = Math.Min(buffer.Length, first.Length - _position);
                first.AsMemory(_position, count).CopyTo(buffer); _position += count; return count;
            }
            await _gate.Task.WaitAsync(cancellationToken);
            var remaining = Math.Min(buffer.Length, rest.Length - (_position - first.Length));
            rest.AsMemory(_position - first.Length, remaining).CopyTo(buffer); _position += remaining; return remaining;
        }
    }
}
