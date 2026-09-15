using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Json.Schema;
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

/// <summary>Real SDK client/server pipe round trips; tools and model stay entirely local.</summary>
public sealed class InternalMcpToolSessionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [Fact]
    public async Task Discovery_preserves_typed_contract_and_invocation_updates_original_snapshot()
    {
        var state = new RecordToolState(() => 71);
        await using var session = await InternalMcpToolSession.CreateAsync([CreateReadFunction(state)]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(session.Tools));

        Assert.Equal("ReadRecords", tool.Name);
        var schema = tool.JsonSchema;
        var properties = schema.GetProperty("properties");
        Assert.Equal("string", properties.GetProperty("query").GetProperty("type").GetString());
        Assert.Equal(new[] { "count", "expense_total", "trend", "plan_completion_rate" },
            properties.GetProperty("metric").GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains(schema.GetProperty("required").EnumerateArray(), x => x.GetString() == "query");
        Assert.Contains(schema.GetProperty("required").EnumerateArray(), x => x.GetString() == "metric");
        Assert.False(properties.TryGetProperty("userId", out _));
        Assert.False(properties.TryGetProperty("cancellationToken", out _));
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());

        var result = await InvokeAsync(tool, ValidArguments());

        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean());
        var evidence = result.GetProperty("structuredContent");
        Assert.Equal(71, evidence.GetProperty("owner").GetInt64());
        Assert.Equal("午饭", evidence.GetProperty("query").GetString());
        Assert.Equal(1, state.Calls);
        Assert.Equal(new RecordEvidence(71, "午饭", RecordAggregateMetric.Count), Assert.Single(state.Snapshot));
    }

    [Fact]
    public async Task Array_output_keeps_natural_schema_and_structure_through_server_wrapper_and_protocol()
    {
        var state = new OutputShapeToolState();
        var function = AIFunctionFactory.Create(state.ReadMany, new AIFunctionFactoryOptions
        {
            Name = "ReadManyRecords",
            SerializerOptions = JsonOptions,
        });
        await using var session = await InternalMcpToolSession.CreateAsync([function]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(session.Tools));
        var advertisedSchema = Assert.IsType<JsonElement>(tool.ReturnJsonSchema);
        Assert.True(JsonElement.DeepEquals(Assert.IsType<JsonElement>(function.ReturnJsonSchema), advertisedSchema));
        Assert.Equal("array", advertisedSchema.GetProperty("type").GetString());

        var result = await InvokeAsync(tool, new());

        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean());
        var structured = result.GetProperty("structuredContent");
        Assert.Equal(JsonValueKind.Array, structured.ValueKind);
        Assert.Equal(2, structured.GetArrayLength());
        Assert.Equal("午饭", structured[0].GetProperty("query").GetString());
        Assert.Equal("晚饭", structured[1].GetProperty("query").GetString());
        Assert.True(JsonSchema.Build(advertisedSchema).Evaluate(structured).IsValid,
            "The discovered array schema must describe the actual result, without an undeclared result wrapper.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nullable_output_preserves_absence_and_present_record_inside_valid_result_object(bool found)
    {
        var state = new OutputShapeToolState();
        var function = AIFunctionFactory.Create(state.ReadOptional, new AIFunctionFactoryOptions
        {
            Name = "ReadOptionalRecord",
            SerializerOptions = JsonOptions,
        });
        await using var session = await InternalMcpToolSession.CreateAsync([function]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(session.Tools));
        var advertisedSchema = Assert.IsType<JsonElement>(tool.ReturnJsonSchema);
        Assert.Equal("object", advertisedSchema.GetProperty("type").GetString());
        Assert.Contains(advertisedSchema.GetProperty("required").EnumerateArray(), x => x.GetString() == "result");
        var schema = JsonSchema.Build(advertisedSchema);

        var result = await InvokeAsync(tool, new() { ["found"] = found });

        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean());
        var structured = result.GetProperty("structuredContent");
        Assert.Equal(JsonValueKind.Object, structured.ValueKind);
        Assert.True(schema.Evaluate(structured).IsValid);
        Assert.False(schema.Evaluate(JsonSerializer.SerializeToElement(new { })).IsValid);
        var payload = structured.GetProperty("result");
        Assert.Equal(found ? JsonValueKind.Object : JsonValueKind.Null, payload.ValueKind);
        if (found)
        {
            Assert.Equal(71, payload.GetProperty("owner").GetInt64());
            Assert.Equal("午饭", payload.GetProperty("query").GetString());
        }
    }

    [Fact]
    public async Task Nullable_result_envelope_relocates_recursive_root_and_definition_references()
    {
        var state = new OutputShapeToolState();
        var original = AIFunctionFactory.Create(state.ReadRecursive, new AIFunctionFactoryOptions
        {
            Name = "ReadRecursiveRecord",
            SerializerOptions = JsonOptions,
        });
        const string sourceSchema = """
            {"type":"object","properties":{"owner":{"$ref":"#/$defs/owner"},"next":{"anyOf":[{"$ref":"#"},{"type":"null"}]}},
            "required":["owner","next"],"$defs":{"owner":{"type":"integer","minimum":1}}}
            """;
        var function = new DeclaredOutputFunction(original, JsonDocument.Parse(sourceSchema).RootElement.Clone());
        await using var session = await InternalMcpToolSession.CreateAsync([function]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(session.Tools));
        var schema = JsonSchema.Build(Assert.IsType<JsonElement>(tool.ReturnJsonSchema));

        var result = await InvokeAsync(tool, new());

        var structured = result.GetProperty("structuredContent");
        Assert.True(schema.Evaluate(structured).IsValid);
        Assert.Equal(82, structured.GetProperty("result").GetProperty("next").GetProperty("owner").GetInt64());
        Assert.False(schema.Evaluate(JsonSerializer.SerializeToElement(new
        {
            result = new { owner = 71, next = new { owner = 0, next = (object?)null } },
        })).IsValid, "The relocated recursive reference must still enforce constraints within the original $defs.");
        Assert.Equal(sourceSchema, function.ReturnJsonSchema!.Value.GetRawText());
    }

    [Fact]
    public async Task Actual_nullable_personal_tools_publish_and_return_explicit_empty_result_without_database_access()
    {
        await using var db = new TraceDbContext(new DbContextOptionsBuilder<TraceDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=must_not_connect;Username=test;Timeout=1", options => options.UseVector()).Options);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "71")], "test")),
        };
        var state = new PersonalRecordTools(db, new CurrentUserContext(new HttpContextAccessor { HttpContext = context }),
            new NoEmbeddingGenerator());
        var registered = new PersonalRecordsCapabilityPackage(state).CreateTools().OfType<AIFunction>().ToArray();
        await using var session = await InternalMcpToolSession.CreateAsync(registered);
        var targets = new[]
        {
            (Name: "GetNavigationTarget", Arguments: new AIFunctionArguments { ["locationId"] = 1L }),
            (Name: "GetMyStorylineEvidence", Arguments: new AIFunctionArguments { ["storylineId"] = Guid.NewGuid().ToString() }),
        };

        foreach (var target in targets)
        {
            var tool = session.Tools.OfType<AIFunction>().Single(x => x.Name == target.Name);
            var schema = JsonSchema.Build(Assert.IsType<JsonElement>(tool.ReturnJsonSchema));
            Assert.False(tool.JsonSchema.GetProperty("properties").TryGetProperty("userId", out _));
            var result = await InvokeAsync(tool, target.Arguments);
            Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean());
            var structured = result.GetProperty("structuredContent");
            Assert.Equal(JsonValueKind.Null, structured.GetProperty("result").ValueKind);
            Assert.True(schema.Evaluate(structured).IsValid);
        }

        Assert.Empty(state.Snapshot.Storylines!);
        Assert.Null(state.Snapshot.NavigationTarget);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("{\"metric\":\"count\"}")]
    [InlineData("{\"query\":\"午饭\"}")]
    [InlineData("{\"query\":\"午饭\",\"metric\":\"sum\"}")]
    [InlineData("{\"query\":\"午饭\",\"metric\":1}")]
    [InlineData("{\"query\":\"午饭\",\"metric\":null}")]
    [InlineData("{\"query\":\"午饭\",\"metric\":\"count\",\"userId\":999}")]
    public async Task Invalid_arguments_return_one_protocol_error_then_stop_without_executing_business_code(string json)
    {
        var state = new RecordToolState(() => 71);
        await using var session = await InternalMcpToolSession.CreateAsync([CreateReadFunction(state)]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(session.Tools));
        var arguments = new AIFunctionArguments(JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!);

        var first = await InvokeAsync(tool, arguments);

        Assert.True(first.GetProperty("isError").GetBoolean());
        Assert.Equal("invalid_tool_arguments", first.GetProperty("structuredContent").GetProperty("code").GetString());
        Assert.NotEmpty(first.GetProperty("content").EnumerateArray());
        await Assert.ThrowsAsync<AssistantToolInvocationException>(() => tool.InvokeAsync(arguments).AsTask());
        Assert.Equal(0, state.Calls);
        Assert.Empty(state.Snapshot);
    }

    [Fact]
    public async Task Corrected_arguments_succeed_after_one_protocol_validation_error()
    {
        var state = new RecordToolState(() => 71);
        await using var session = await InternalMcpToolSession.CreateAsync([CreateReadFunction(state)]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(session.Tools));

        var invalid = await InvokeAsync(tool, new AIFunctionArguments { ["query"] = "午饭", ["metric"] = "sum" });
        var corrected = await InvokeAsync(tool, ValidArguments());

        Assert.True(invalid.GetProperty("isError").GetBoolean());
        Assert.Equal(71, corrected.GetProperty("structuredContent").GetProperty("owner").GetInt64());
        Assert.Equal(1, state.Calls);
    }

    [Fact]
    public async Task Separate_sessions_retain_their_request_subject_and_snapshot_after_caller_context_returns()
    {
        // HttpContextAccessor uses AsyncLocal, as the application's JWT CurrentUserContext does.
        // Constructing each session in its own async context prevents one subject replacing another.
        var first = await CreateOwnedSessionAsync(71);
        await using var firstSession = first.Session;
        var second = await CreateOwnedSessionAsync(82);
        await using var secondSession = second.Session;
        Assert.Null(new HttpContextAccessor().HttpContext);

        var results = await Task.WhenAll(
            InvokeAsync(Assert.IsAssignableFrom<AIFunction>(Assert.Single(first.Session.Tools)), ValidArguments()),
            InvokeAsync(Assert.IsAssignableFrom<AIFunction>(Assert.Single(second.Session.Tools)), ValidArguments("晚饭")));

        Assert.Equal(71, results[0].GetProperty("structuredContent").GetProperty("owner").GetInt64());
        Assert.Equal(82, results[1].GetProperty("structuredContent").GetProperty("owner").GetInt64());
        Assert.Equal(new RecordEvidence(71, "午饭", RecordAggregateMetric.Count), Assert.Single(first.State.Snapshot));
        Assert.Equal(new RecordEvidence(82, "晚饭", RecordAggregateMetric.Count), Assert.Single(second.State.Snapshot));
    }

    [Fact]
    public async Task Runtime_failure_is_terminal_and_does_not_expose_private_exception_details()
    {
        var calls = 0;
        var function = AIFunctionFactory.Create((Func<string>)(() =>
        {
            calls++;
            throw new InvalidOperationException("private-database-connection-and-record-content");
        }), new AIFunctionFactoryOptions { Name = "FailRead", SerializerOptions = JsonOptions });
        await using var session = await InternalMcpToolSession.CreateAsync([function]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(session.Tools));

        var error = await Assert.ThrowsAsync<AssistantToolInvocationException>(() => tool.InvokeAsync(new()).AsTask());

        Assert.Equal(1, calls);
        Assert.DoesNotContain("private-database", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_interrupts_running_tool_and_preserves_operation_canceled(bool cancelSession)
    {
        var state = new WaitingToolState();
        using var cancellation = new CancellationTokenSource();
        await using var session = await InternalMcpToolSession.CreateAsync([CreateWaitingFunction(state)],
            cancelSession ? cancellation.Token : CancellationToken.None);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(session.Tools));
        var invocation = tool.InvokeAsync(new(), cancelSession ? CancellationToken.None : cancellation.Token).AsTask();
        await state.Started.Task.WaitAsync(TestTimeout);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.WaitAsync(TestTimeout));
        await state.Stopped.Task.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Disposal_cancels_running_tool_is_idempotent_and_rejects_later_invocations()
    {
        var state = new WaitingToolState();
        var session = await InternalMcpToolSession.CreateAsync([CreateWaitingFunction(state)]);
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(session.Tools));
        var invocation = tool.InvokeAsync(new()).AsTask();
        await state.Started.Task.WaitAsync(TestTimeout);

        await session.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        await state.Stopped.Task.WaitAsync(TestTimeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.WaitAsync(TestTimeout));
        await session.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => tool.InvokeAsync(new()).AsTask());
    }

    [Fact]
    public async Task Agent_invokes_discovered_tool_once_then_receives_usable_protocol_result_and_answers()
    {
        var state = new RecordToolState(() => 71);
        await using var mcp = await InternalMcpToolSession.CreateAsync([CreateReadFunction(state)]);
        using var model = new LocalToolChatClient(repeatInvalid: false);
        var agent = CreateAgent(model, mcp.Tools);
        var conversation = await agent.CreateSessionAsync();
        var answer = new StringBuilder();

        await foreach (var update in agent.RunStreamingAsync("查找午饭记录", conversation))
        {
            AssistantToolInvocationException.ThrowIfPresent(update.Contents);
            answer.Append(update.Text);
        }

        Assert.Equal("已找到本人的午饭记录。", answer.ToString());
        Assert.Equal(2, model.Calls);
        Assert.Equal(1, model.ToolResults);
        Assert.Equal(1, state.Calls);
        Assert.Single(state.Snapshot);
    }

    [Fact]
    public async Task Agent_stops_after_second_invalid_call_before_another_model_request()
    {
        var state = new RecordToolState(() => 71);
        await using var mcp = await InternalMcpToolSession.CreateAsync([CreateReadFunction(state)]);
        using var model = new LocalToolChatClient(repeatInvalid: true);
        var agent = CreateAgent(model, mcp.Tools);
        var conversation = await agent.CreateSessionAsync();

        await Assert.ThrowsAsync<AssistantToolInvocationException>(async () =>
        {
            await foreach (var update in agent.RunStreamingAsync("查找午饭记录", conversation))
                AssistantToolInvocationException.ThrowIfPresent(update.Contents);
        });

        Assert.Equal(2, model.Calls);
        Assert.Equal(0, state.Calls);
        Assert.Empty(state.Snapshot);
    }

    private static ChatClientAgent CreateAgent(IChatClient model, IReadOnlyList<AITool> tools) =>
        new(model, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Tools = [.. tools] },
            AllowConcurrentInvocation = false,
        });

    private static AIFunction CreateReadFunction(RecordToolState state) =>
        AIFunctionFactory.Create(state.ReadAsync, new AIFunctionFactoryOptions
        {
            Name = "ReadRecords",
            Description = "搜索当前用户自己的记录。",
            SerializerOptions = JsonOptions,
        });

    private static AIFunction CreateWaitingFunction(WaitingToolState state) =>
        AIFunctionFactory.Create(state.WaitAsync, new AIFunctionFactoryOptions
        {
            Name = "WaitForRecords",
            SerializerOptions = JsonOptions,
        });

    private static AIFunctionArguments ValidArguments(string query = "午饭") =>
        new() { ["query"] = query, ["metric"] = "count" };

    private static async Task<JsonElement> InvokeAsync(AIFunction tool, AIFunctionArguments arguments) =>
        Assert.IsType<JsonElement>(await tool.InvokeAsync(arguments).AsTask().WaitAsync(TestTimeout));

    private static async Task<(InternalMcpToolSession Session, RecordToolState State)> CreateOwnedSessionAsync(long subject)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject.ToString())], "test")),
            },
        };
        var currentUser = new CurrentUserContext(accessor);
        var state = new RecordToolState(() => currentUser.UserId);
        var session = await InternalMcpToolSession.CreateAsync([CreateReadFunction(state)]);
        return (session, state);
    }

    private sealed record RecordEvidence(long Owner, string Query, RecordAggregateMetric Metric);

    private sealed class OutputShapeToolState
    {
        public IReadOnlyList<RecordEvidence> ReadMany() =>
        [
            new(71, "午饭", RecordAggregateMetric.Count),
            new(71, "晚饭", RecordAggregateMetric.Count),
        ];

        public RecordEvidence? ReadOptional(bool found) =>
            found ? new RecordEvidence(71, "午饭", RecordAggregateMetric.Count) : null;

        public RecursiveRecord? ReadRecursive() => new(71, new(82, null));
    }

    private sealed record RecursiveRecord(long Owner, RecursiveRecord? Next);

    private sealed class DeclaredOutputFunction(AIFunction inner, JsonElement schema) : DelegatingAIFunction(inner)
    {
        public override JsonElement? ReturnJsonSchema => schema;
    }

    private sealed class NoEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("MCP contract tests must not request embeddings.");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class RecordToolState(Func<long> currentSubject)
    {
        public List<RecordEvidence> Snapshot { get; } = [];
        public int Calls { get; private set; }

        public async Task<RecordEvidence> ReadAsync(string query, RecordAggregateMetric metric,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = new RecordEvidence(currentSubject(), query, metric);
            Snapshot.Add(evidence);
            return evidence;
        }
    }

    private sealed class WaitingToolState
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> WaitAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "unreachable";
            }
            finally { Stopped.TrySetResult(); }
        }
    }

    private sealed class LocalToolChatClient(bool repeatInvalid) : IChatClient
    {
        public int Calls { get; private set; }
        public int ToolResults { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Assert.True(Calls <= 2, "The model must not silently retry terminal MCP failures.");
            Assert.Equal("ReadRecords", Assert.Single(options!.Tools!).Name);
            if (Calls == 1 || repeatInvalid)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new FunctionCallContent($"read-{Calls}", "ReadRecords",
                        new Dictionary<string, object?> { ["query"] = "午饭", ["metric"] = repeatInvalid ? "sum" : "count" })],
                    FinishReason = ChatFinishReason.ToolCalls,
                };
            }
            else
            {
                var result = Assert.Single(messages.SelectMany(x => x.Contents).OfType<FunctionResultContent>());
                Assert.Null(result.Exception);
                var envelope = Assert.IsType<JsonElement>(result.Result);
                Assert.False(envelope.TryGetProperty("isError", out var error) && error.GetBoolean());
                Assert.Equal(71, envelope.GetProperty("structuredContent").GetProperty("owner").GetInt64());
                Assert.Contains(envelope.GetProperty("content").EnumerateArray(),
                    block => block.GetProperty("type").GetString() == "text");
                ToolResults++;
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent("已找到本人的午饭记录。")],
                    FinishReason = ChatFinishReason.Stop,
                };
            }
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
