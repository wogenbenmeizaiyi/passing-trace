using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PassingTrace.Events.Api.Ai.Capabilities;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class ValidatedMcpServerToolTests
{
    private const string SearchSchema = """
        {
          "type":"object",
          "properties":{
            "query":{"type":"string","minLength":1},
            "limit":{"$ref":"#/$defs/limit"},
            "mode":{"type":"string","enum":["recent","all"]},
            "date":{"type":"string","format":"date-time"},
            "tag":{"type":["string","null"]}
          },
          "required":["query"],
          "$defs":{"limit":{"type":"integer","minimum":1,"maximum":3}},
          "additionalProperties":true
        }
        """;

    [Fact]
    public void Discovery_ClonesSchemaAndMetadata_AndRejectsAdditionalRootArguments()
    {
        var inner = new FakeTool(SearchSchema, (_, _) => ValueTask.FromResult(Success()));
        inner.ProtocolTool.Title = "Search records";
        inner.ProtocolTool.Annotations = new() { ReadOnlyHint = true, DestructiveHint = false };
        var originalSchema = inner.ProtocolTool.InputSchema.GetRawText();

        var tool = new ValidatedMcpServerTool(inner);

        Assert.NotSame(inner.ProtocolTool, tool.ProtocolTool);
        Assert.Equal(originalSchema, inner.ProtocolTool.InputSchema.GetRawText());
        Assert.False(tool.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("Search records", tool.ProtocolTool.Title);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
        Assert.NotSame(inner.ProtocolTool.Annotations, tool.ProtocolTool.Annotations);
        Assert.Same(inner.Metadata, tool.Metadata);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"query":null}""")]
    [InlineData("""{"query":123}""")]
    [InlineData("""{"query":""}""")]
    [InlineData("""{"query":"private-search","limit":"2"}""")]
    [InlineData("""{"query":"private-search","limit":1.5}""")]
    [InlineData("""{"query":"private-search","limit":0}""")]
    [InlineData("""{"query":"private-search","limit":4}""")]
    [InlineData("""{"query":"private-search","mode":"private-enum"}""")]
    [InlineData("""{"query":"private-search","date":"private-invalid-date"}""")]
    [InlineData("""{"query":"private-search","date":"2026-02-30T00:00:00Z"}""")]
    [InlineData("""{"query":"private-search","userId":"private-user"}""")]
    public async Task InvalidArguments_AreRejectedBeforeDispatch_WithSafeStructuredError(string json)
    {
        var calls = 0;
        var inner = new FakeTool(SearchSchema, (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(Success());
        });
        var tool = new ValidatedMcpServerTool(inner);
        await using var server = CreateServer();

        var result = await tool.InvokeAsync(Request(server, json));

        AssertSafeError(result, "invalid_tool_arguments");
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ValidArguments_PreserveTheSameRequestServicesCancellationAndResult()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var server = CreateServer();
        using var cancellation = new CancellationTokenSource();
        var request = Request(server, """
            {"query":"private-search","limit":2,"mode":"recent","date":"2026-09-14T10:30:00+08:00","tag":null}
            """);
        request.Services = services;
        var expected = Success();
        var calls = 0;
        var inner = new FakeTool(SearchSchema, (actualRequest, actualCancellation) =>
        {
            calls++;
            Assert.Same(request, actualRequest);
            Assert.Same(services, actualRequest.Services);
            Assert.Equal(cancellation.Token, actualCancellation);
            return ValueTask.FromResult(expected);
        });

        var result = await new ValidatedMcpServerTool(inner).InvokeAsync(request, cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MissingArguments_AreAnEmptyObject_ForParameterlessTools()
    {
        await using var server = CreateServer();
        var calls = 0;
        var inner = new FakeTool("""{"type":"object"}""", (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(Success());
        });

        var result = await new ValidatedMcpServerTool(inner).InvokeAsync(Request(server, null));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DelegatePropertyAnnotations_AreEnforcedByGeneratedSchema()
    {
        await using var server = CreateServer();
        var calls = 0;
        var inner = McpServerTool.Create((AnnotatedArguments input) =>
        {
            calls++;
            return Success();
        }, new() { Name = "annotated" });
        var tool = new ValidatedMcpServerTool(inner);

        foreach (var json in new[]
        {
            """{"input":{"limit":1,"status":"ready"}}""",
            """{"input":{"query":"private-search","limit":1,"status":"ready"}}""",
            """{"input":{"query":"ok","limit":4,"status":"ready"}}""",
            """{"input":{"query":"ok","limit":1,"status":"private-status"}}""",
        })
        {
            AssertSafeError(await tool.InvokeAsync(Request(server, json)), "invalid_tool_arguments");
        }
        Assert.Equal(0, calls);

        var accepted = await tool.InvokeAsync(Request(server,
            """{"input":{"query":"ok","limit":2,"status":"ready"}}"""));

        Assert.NotEqual(true, accepted.IsError);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvocationExceptions_AreSanitized_IncludingMcpExceptions(bool protocolException)
    {
        await using var server = CreateServer();
        var inner = new FakeTool(SearchSchema, (_, _) => throw (protocolException
            ? new McpException("private-user private-search private-evidence")
            : new InvalidOperationException("private-user private-search private-evidence")));

        var result = await new ValidatedMcpServerTool(inner).InvokeAsync(
            Request(server, """{"query":"private-search"}"""));

        AssertSafeError(result, "tool_unavailable");
    }

    [Fact]
    public async Task ReturnedToolErrors_AreSanitizedBeforeReachingTheClient()
    {
        await using var server = CreateServer();
        var inner = new FakeTool(SearchSchema, (_, _) => ValueTask.FromResult(new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "private-evidence" }],
            StructuredContent = JsonSerializer.SerializeToElement(new { reason = "private-user" }),
        }));

        var result = await new ValidatedMcpServerTool(inner).InvokeAsync(
            Request(server, """{"query":"private-search"}"""));

        AssertSafeError(result, "tool_unavailable");
    }

    [Fact]
    public async Task AlreadyCancelledCall_PropagatesBeforeValidationOrDispatch()
    {
        await using var server = CreateServer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var tool = new ValidatedMcpServerTool(new FakeTool(SearchSchema, (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(Success());
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await tool.InvokeAsync(Request(server, "{}"), cancellation.Token));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InnerCancellation_PropagatesWithoutBecomingAToolError()
    {
        await using var server = CreateServer();
        var cancellation = new OperationCanceledException("private-cancellation");
        var tool = new ValidatedMcpServerTool(new FakeTool(SearchSchema, (_, _) => throw cancellation));

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await tool.InvokeAsync(Request(server, """{"query":"private-search"}""")));

        Assert.Same(cancellation, actual);
    }

    private static void AssertSafeError(CallToolResult result, string code)
    {
        Assert.True(result.IsError);
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(code, structured.GetProperty("code").GetString());
        Assert.Single(structured.EnumerateObject());
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.False(string.IsNullOrWhiteSpace(content.Text));
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions));
    }

    private static CallToolResult Success() => new() { Content = [] };

    private static McpServer CreateServer() => McpServer.Create(
        new StreamServerTransport(Stream.Null, Stream.Null),
        new McpServerOptions
        {
            ServerInfo = new() { Name = "validation-tests", Version = "1.0.0" },
            ScopeRequests = false,
        });

    private static RequestContext<CallToolRequestParams> Request(McpServer server, string? json) =>
        new(server, new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.ToolsCall }, new()
        {
            Name = "test",
            Arguments = json is null ? null : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json),
        });

    public sealed class AnnotatedArguments
    {
        [Required, MaxLength(5)]
        public string? Query { get; init; }

        [Range(1, 3)]
        public int Limit { get; init; }

        [RegularExpression("^(draft|ready)$")]
        public string? Status { get; init; }
    }

    private sealed class FakeTool(
        string schema,
        Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>> invoke)
        : McpServerTool
    {
        public override Tool ProtocolTool { get; } = new()
        {
            Name = "test",
            InputSchema = JsonSerializer.Deserialize<JsonElement>(schema),
        };

        public override IReadOnlyList<object> Metadata { get; } = [new object()];

        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) =>
            invoke(request, cancellationToken);
    }
}
