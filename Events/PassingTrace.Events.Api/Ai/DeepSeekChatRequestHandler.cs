using System.Text;
using System.Text.Json.Nodes;

namespace PassingTrace.Events.Api.Ai;

/// <summary>
/// Official DeepSeek Chat Completions compatibility. Our persisted history does not retain
/// provider reasoning_content, so explicitly use non-thinking mode for reliable tool continuations.
/// No request body, response body or credential is logged by this adapter.
/// </summary>
public sealed class DeepSeekChatRequestHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && request.RequestUri is { Scheme: "https" } uri &&
            uri.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal) && request.Content is { } original)
        {
            var body = JsonNode.Parse(await original.ReadAsStringAsync(cancellationToken))!.AsObject();
            body["thinking"] = new JsonObject { ["type"] = "disabled" };
            body["reasoning_effort"] = "none";
            if (body.ContainsKey("max_completion_tokens"))
            {
                if (!body.ContainsKey("max_tokens")) body["max_tokens"] = body["max_completion_tokens"]?.DeepClone();
                body.Remove("max_completion_tokens");
            }
            if (body["messages"] is JsonArray messages)
            {
                foreach (var message in messages.OfType<JsonObject>())
                    if (message["role"]?.GetValue<string>() == "developer") message["role"] = "system";
            }
            var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            foreach (var header in original.Headers)
                if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    !header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Content = content;
            original.Dispose();
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
