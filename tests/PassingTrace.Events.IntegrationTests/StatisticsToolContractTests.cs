using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Infrastructure;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class StatisticsToolContractTests
{
    [Fact]
    public void Registered_tool_exposes_named_enum_and_complete_statistical_instructions()
    {
        using var db = CreateDisconnectedDb();
        var function = CreateTool(db, out _);

        Assert.Contains("expense_total", function.Description);
        Assert.Contains("所有记录", function.Description);
        var schema = function.JsonSchema;
        var metric = schema.GetProperty("properties").GetProperty("metric");
        Assert.Equal("string", metric.GetProperty("type").GetString());
        Assert.Equal(new[] { "count", "expense_total", "trend", "plan_completion_rate" },
            metric.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("金额合计", metric.GetProperty("description").GetString());
        Assert.Contains(schema.GetProperty("required").EnumerateArray(), value => value.GetString() == "metric");
        Assert.False(schema.GetProperty("properties").TryGetProperty("userId", out _));
    }

    [Theory]
    [InlineData("sum")]
    [InlineData(1)]
    [InlineData("1")]
    [InlineData(null)]
    public async Task Malformed_metric_returns_one_correction_without_database_access_or_false_evidence(object? value)
    {
        await using var db = CreateDisconnectedDb();
        var function = CreateTool(db, out var tools);
        var arguments = new AIFunctionArguments { ["metric"] = JsonSerializer.SerializeToElement(value) };

        var response = Assert.IsType<JsonElement>(await function.InvokeAsync(arguments));

        Assert.False(response.GetProperty("success").GetBoolean());
        Assert.Equal("invalid_statistics_metric", response.GetProperty("error").GetString());
        Assert.Equal(4, response.GetProperty("allowedMetrics").GetArrayLength());
        Assert.Null(tools.Snapshot.Aggregate);
        Assert.Empty(db.ChangeTracker.Entries());
        await Assert.ThrowsAsync<AssistantStatisticsToolException>(() => function.InvokeAsync(arguments).AsTask());
    }

    [Fact]
    public async Task Missing_metric_does_not_default_to_count()
    {
        await using var db = CreateDisconnectedDb();
        var function = CreateTool(db, out var tools);
        var response = Assert.IsType<JsonElement>(await function.InvokeAsync(new AIFunctionArguments()));

        Assert.False(response.GetProperty("success").GetBoolean());
        Assert.Null(tools.Snapshot.Aggregate);
    }

    [Fact]
    public async Task Repeated_invalid_metric_stops_streaming_loop_with_a_safe_statistics_error()
    {
        await using var db = CreateDisconnectedDb();
        var function = CreateTool(db, out _);
        using var provider = new RepeatingInvalidStatisticsClient();
        using var client = new FunctionInvokingChatClient(provider);

        await Assert.ThrowsAsync<AssistantStatisticsToolException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "统计所有消费金额")], new ChatOptions { Tools = [function] }))
                AssistantStatisticsToolException.ThrowIfPresent(update.Contents);
        });
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Agent_stream_surfaces_terminal_statistics_failure_before_provider_retries()
    {
        await using var db = CreateDisconnectedDb();
        var function = CreateTool(db, out _);
        using var provider = new RepeatingInvalidStatisticsClient();
        var agent = new ChatClientAgent(provider, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Tools = [function] },
            AllowConcurrentInvocation = false,
        });
        var session = await agent.CreateSessionAsync();

        await Assert.ThrowsAsync<AssistantStatisticsToolException>(async () =>
        {
            await foreach (var update in agent.RunStreamingAsync("统计所有消费金额", session))
                AssistantStatisticsToolException.ThrowIfPresent(update.Contents);
        });
        Assert.Equal(2, provider.Calls);
    }

    private static TraceDbContext CreateDisconnectedDb() => new(new DbContextOptionsBuilder<TraceDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=must_not_connect;Username=test;Timeout=1", options => options.UseVector()).Options);

    private static AIFunction CreateTool(TraceDbContext db, out PersonalRecordTools tools)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "91999")], "test")),
        };
        tools = new PersonalRecordTools(db, new CurrentUserContext(new HttpContextAccessor { HttpContext = context }),
            new UnavailableEmbeddingGenerator());
        return new PersonalRecordsCapabilityPackage(tools).CreateTools().OfType<AIFunction>()
            .Single(tool => tool.Name == "AggregateMyRecords");
    }

    private sealed class RepeatingInvalidStatisticsClient : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls > 6) throw new InvalidOperationException("The tool retry loop was not bounded.");
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new FunctionCallContent($"call-{Calls}", "AggregateMyRecords",
                    new Dictionary<string, object?> { ["metric"] = "sum" })],
                FinishReason = ChatFinishReason.ToolCalls,
            };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class UnavailableEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Statistics must not request embeddings.");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
