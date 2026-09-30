using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace PassingTrace.Events.Api.Ai.Capabilities;

internal static class AiFunctionToolFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static AIFunction Create<T>(T target, string methodName, string name, string? description = null)
        where T : class =>
        AIFunctionFactory.Create(typeof(T).GetMethod(methodName)!, target, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = JsonOptions,
            JsonSchemaCreateOptions = new AIJsonSchemaCreateOptions
            {
                TransformSchemaNode = (context, schema) =>
                {
                    if (context.GetCustomAttribute<DataTypeAttribute>()?.DataType == DataType.DateTime)
                        schema["format"] = "date-time";
                    return schema;
                },
            },
        });
}
