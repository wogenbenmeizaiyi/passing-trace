using Microsoft.Extensions.AI;

namespace PassingTrace.Events.Api.Ai;

/// <summary>A statistics tool could not produce a result; this is not invalid user input.</summary>
public sealed class AssistantStatisticsToolException()
    : InvalidOperationException("暂时无法完成这次统计，请稍后重试。")
{
    /// <summary>Stop the stream before the SDK silently retries a terminal statistics failure.</summary>
    public static void ThrowIfPresent(IEnumerable<AIContent> contents)
    {
        foreach (var result in contents.OfType<FunctionResultContent>())
        {
            for (var exception = result.Exception; exception is not null; exception = exception.InnerException)
                if (exception is AssistantStatisticsToolException) throw new AssistantStatisticsToolException();
        }
    }
}
