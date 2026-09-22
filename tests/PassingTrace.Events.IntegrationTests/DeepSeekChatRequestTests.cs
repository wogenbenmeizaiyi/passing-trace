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

public sealed class DeepSeekChatRequestTests
{
    [Theory]
    [InlineData("https://api.deepseek.com/v1/chat/completions")]
    [InlineData("https://api.deepseek.com/chat/completions")]
    public async Task Official_endpoint_disables_thinking_preserves_tools_and_maps_sdk_fields(string endpoint)
    {
        var capture = new CaptureHandler();
        using var http = new HttpClient(new DeepSeekChatRequestHandler(capture));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent("""
                {"model":"deepseek-flash","messages":[{"role":"developer","content":"rules"},
                  {"role":"user","content":"hello"},{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","type":"function","function":{"name":"ReadAssistantSkill","arguments":"{}"}}]},
                  {"role":"tool","tool_call_id":"call-1","content":"rules loaded"}],
                 "max_completion_tokens":64,"tools":[{"type":"function","function":{"name":"ReadAssistantSkill"}}],
                 "thinking":{"type":"enabled"},"reasoning_effort":"high","stream":true}
                """, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", "test-placeholder");
        using var response = await http.SendAsync(request);
        var body = capture.Bodies.Single();
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("none", body.GetProperty("reasoning_effort").GetString());
        Assert.Equal(64, body.GetProperty("max_tokens").GetInt32());
        Assert.False(body.TryGetProperty("max_completion_tokens", out _));
        Assert.Equal("system", body.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("call-1", body.GetProperty("messages")[3].GetProperty("tool_call_id").GetString());
        Assert.Equal("ReadAssistantSkill", body.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.True(body.GetProperty("stream").GetBoolean());
        Assert.Equal("Bearer", capture.AuthorizationScheme);
    }

    [Theory]
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions")]
    [InlineData("https://api.minimaxi.com/v1/chat/completions")]
    [InlineData("https://api.deepseek.com/v1/embeddings")]
    [InlineData("https://api.deepseek.com.other.example/v1/chat/completions")]
    public async Task Other_providers_and_embeddings_are_unchanged(string endpoint)
    {
        var capture = new CaptureHandler();
        using var http = new HttpClient(new DeepSeekChatRequestHandler(capture));
        using var response = await http.PostAsync(endpoint,
            new StringContent("{\"model\":\"unchanged\",\"max_completion_tokens\":64}", Encoding.UTF8, "application/json"));
        var body = capture.Bodies.Single();
        Assert.Equal(64, body.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.False(body.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task Actual_openai_sdk_completes_multistep_tool_roundtrip_with_deepseek_settings()
    {
        var capture = new CaptureHandler(streamTools: true);
        using var http = new HttpClient(new OpenAiCompatibleStreamingHandler(new DeepSeekChatRequestHandler(capture)));
        var sdk = new OpenAIClient(new ApiKeyCredential("test-placeholder"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.deepseek.com/v1/"),
            Transport = new HttpClientPipelineTransport(http),
        });
        using var client = sdk.GetChatClient("deepseek-flash").AsIChatClient().AsBuilder().UseFunctionInvocation().Build();
        var calls = 0;
        var tool = AIFunctionFactory.Create(() => { calls++; return "loaded"; }, "ReadAssistantSkill");
        var text = new StringBuilder();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions { Tools = [tool], MaxOutputTokens = 64 })) text.Append(update.Text);
        Assert.Equal("ready", text.ToString());
        Assert.Equal(1, calls);
        Assert.Equal(2, capture.Bodies.Count);
        Assert.All(capture.Bodies, body =>
        {
            Assert.Equal("none", body.GetProperty("reasoning_effort").GetString());
            Assert.Equal(64, body.GetProperty("max_tokens").GetInt32());
        });
        Assert.Contains(capture.Bodies[1].GetProperty("messages").EnumerateArray(),
            message => message.GetProperty("role").GetString() == "tool");
    }

    private sealed class CaptureHandler(bool streamTools = false) : HttpMessageHandler
    {
        public List<JsonElement> Bodies { get; } = [];
        public string? AuthorizationScheme { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            Bodies.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken)));
            if (!streamTools) return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
            var payload = Bodies.Count == 1
                ? """{"id":"response-1","object":"chat.completion.chunk","model":"deepseek-flash","created":1,"choices":[{"index":0,"delta":{"role":"assistant","tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"ReadAssistantSkill","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}"""
                : """{"id":"response-2","object":"chat.completion.chunk","model":"deepseek-flash","created":1,"choices":[{"index":0,"delta":{"role":"assistant","content":"ready"},"finish_reason":"stop"}]}""";
            return new(HttpStatusCode.OK) { Content = new StringContent($"data: {payload}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }
}
