using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace PassingTrace.Events.Api.Ai.Capabilities;

/// <summary>
/// Keeps optional values inside a non-null MCP result object. The SDK's nullable
/// StructuredContent property cannot preserve the distinction between JSON null and absence.
/// </summary>
internal sealed class NullableMcpResultFunction : DelegatingAIFunction
{
    private readonly JsonElement _returnSchema;

    private NullableMcpResultFunction(AIFunction innerFunction) : base(innerFunction) =>
        _returnSchema = CreateReturnSchema(innerFunction.ReturnJsonSchema);

    public override JsonElement? ReturnJsonSchema => _returnSchema;

    public static AIFunction WrapIfNullable(AIFunction function)
    {
        if (function is NullableMcpResultFunction || function.UnderlyingMethod is not { } method)
            return function;

        var returnType = method.ReturnType;
        var nullability = new NullabilityInfoContext().Create(method.ReturnParameter);
        if (returnType.IsGenericType &&
            (returnType.GetGenericTypeDefinition() == typeof(Task<>) ||
             returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            returnType = returnType.GetGenericArguments()[0];
            nullability = nullability.GenericTypeArguments[0];
        }

        return Nullable.GetUnderlyingType(returnType) is not null ||
            (!returnType.IsValueType && nullability.ReadState == NullabilityState.Nullable)
            ? new NullableMcpResultFunction(function)
            : function;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await InnerFunction.InvokeAsync(arguments, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var value = JsonSerializer.SerializeToNode(result, JsonSerializerOptions);
        // JsonObject retains the explicit null even if the function's serializer omits null properties.
        return JsonSerializer.SerializeToElement(new JsonObject { ["result"] = value });
    }

    private static JsonElement CreateReturnSchema(JsonElement? originalSchema)
    {
        var valueSchema = originalSchema is { } schema ? JsonNode.Parse(schema.GetRawText())! : new JsonObject();
        RelocateLocalReferences(valueSchema);
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["result"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray
                    {
                        new JsonObject { ["$ref"] = "#/$defs/resultValue" },
                        new JsonObject { ["type"] = "null" },
                    },
                },
            },
            ["required"] = new JsonArray("result"),
            ["additionalProperties"] = false,
            ["$defs"] = new JsonObject { ["resultValue"] = valueSchema },
        });
    }

    private static void RelocateLocalReferences(JsonNode? node)
    {
        if (node is JsonObject schema)
        {
            // A nested resource owns its reference base and is unaffected by the outer envelope.
            if (schema.ContainsKey("$id")) return;
            foreach (var keyword in new[] { "$ref", "$dynamicRef" })
            {
                if (schema[keyword] is JsonValue value && value.TryGetValue<string>(out var reference) &&
                    (reference == "#" || reference.StartsWith("#/", StringComparison.Ordinal)))
                    schema[keyword] = "#/$defs/resultValue" + reference[1..];
            }
            foreach (var property in schema) RelocateLocalReferences(property.Value);
        }
        else if (node is JsonArray array)
            foreach (var item in array) RelocateLocalReferences(item);
    }
}
