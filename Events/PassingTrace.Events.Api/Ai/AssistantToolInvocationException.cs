using Microsoft.Extensions.AI;

namespace PassingTrace.Events.Api.Ai;

public sealed class AssistantToolInvocationException() : Exception("暂时没能完成这次记录查询，请稍后重试。")
{
    public static void ThrowIfPresent(IEnumerable<AIContent> contents)
    {
        foreach (var result in contents.OfType<FunctionResultContent>())
            for (var error = result.Exception; error is not null; error = error.InnerException)
                if (error is AssistantToolInvocationException) throw new AssistantToolInvocationException();
    }
}
