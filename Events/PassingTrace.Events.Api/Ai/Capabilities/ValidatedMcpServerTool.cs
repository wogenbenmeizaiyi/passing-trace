using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PassingTrace.Events.Api.Ai.Capabilities;

/// <summary>
/// Enforces the advertised JSON Schema before dispatching to the request's existing tool instance.
/// Validation details and exceptions stay private because they may contain personal arguments.
/// </summary>
public sealed class ValidatedMcpServerTool(McpServerTool inner) : McpServerTool
{
    private readonly (Tool Tool, JsonSchema Schema) _definition = CreateDefinition(inner);

    public override Tool ProtocolTool => _definition.Tool;

    public override IReadOnlyList<object> Metadata => inner.Metadata;

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (request.Params is null)
                return InvalidArguments();

            var arguments = JsonSerializer.SerializeToElement(
                request.Params.Arguments ?? new Dictionary<string, JsonElement>(), McpJsonUtilities.DefaultOptions);
            var validation = _definition.Schema.Evaluate(arguments, new EvaluationOptions
            {
                RequireFormatValidation = true,
                OutputFormat = OutputFormat.Flag,
            });
            if (!validation.IsValid)
                return InvalidArguments();

            cancellationToken.ThrowIfCancellationRequested();
            var result = await inner.InvokeAsync(request, cancellationToken);
            return result.IsError == true ? Unavailable() : result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Never expose or log exception messages, which can contain search terms or evidence.
            return Unavailable();
        }
    }

    private static (Tool Tool, JsonSchema Schema) CreateDefinition(McpServerTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var tool = JsonSerializer.Deserialize<Tool>(
            JsonSerializer.SerializeToElement(inner.ProtocolTool, McpJsonUtilities.DefaultOptions),
            McpJsonUtilities.DefaultOptions)!;
        var inputSchema = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
        inputSchema["additionalProperties"] = false;
        tool.InputSchema = JsonSerializer.SerializeToElement(inputSchema);
        var schema = JsonSchema.Build(tool.InputSchema, new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new(),
        });
        return (tool, schema);
    }

    private static CallToolResult InvalidArguments() =>
        Error("invalid_tool_arguments", "Tool arguments do not match the input schema.");

    private static CallToolResult Unavailable() =>
        Error("tool_unavailable", "The tool is temporarily unavailable.");

    private static CallToolResult Error(string code, string message) => new()
    {
        IsError = true,
        StructuredContent = JsonSerializer.SerializeToElement(new { code }),
        Content = [new TextContentBlock { Text = message }],
    };
}
