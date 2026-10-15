using Microsoft.Extensions.AI;

namespace PassingTrace.Events.Api.Ai.Capabilities;

public sealed class AssistantToolInvocationException(string? errorCode = null, bool isWriteTool = false)
    : Exception(isWriteTool ? "暂时没能完成这次记录操作，请稍后重试。" : "暂时没能完成这次记录查询，请稍后重试。")
{
    public string? ErrorCode { get; } = errorCode;
    public bool IsWriteTool { get; } = isWriteTool;

    public static void ThrowIfPresent(IEnumerable<AIContent> contents)
    {
        foreach (var result in contents.OfType<FunctionResultContent>())
            for (var error = result.Exception; error is not null; error = error.InnerException)
                if (error is AssistantToolInvocationException invocationError) throw invocationError;
    }
}
