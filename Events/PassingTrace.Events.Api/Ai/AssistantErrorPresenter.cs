using System.ClientModel;

namespace PassingTrace.Events.Api.Ai;

public sealed record AssistantErrorResponse(string Code, string Message, bool Retryable);

public static class AssistantErrorPresenter
{
    public static AssistantErrorResponse Present(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is KeyNotFoundException)
        {
            return new AssistantErrorResponse(
                "conversation_not_found",
                "这段对话已经不存在，请新建会话后再试。",
                false);
        }

        if (exception is ClientResultException clientError && clientError.Status == 429)
        {
            return new AssistantErrorResponse(
                "ai_busy",
                "现在提问的人有点多，请稍等片刻再试。",
                true);
        }

        if (ContainsMalformedStreamingProtocol(exception))
        {
            return new AssistantErrorResponse(
                "incomplete_ai_response",
                "AI 服务刚才返回了不完整结果，请重新发送一次。",
                true);
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AssistantToolInvocationException)
                return new AssistantErrorResponse(
                    "record_tools_unavailable",
                    "暂时没能完成这次记录查询，请稍后重试。",
                    true);
            if (current is AssistantResponseLimitException)
                return new AssistantErrorResponse(
                    "ai_response_too_long",
                    "这次回答内容较多，没能完整生成。请缩小范围后重试。",
                    true);
            if (current is AssistantResponseBlockedException)
                return new AssistantErrorResponse(
                    "ai_response_blocked",
                    "AI 服务未能提供这次回答，请换一种方式提问。",
                    false);
        }

        if (ContainsStatisticsToolError(exception))
        {
            return new AssistantErrorResponse(
                "statistics_unavailable",
                "暂时没能完成消费或记录统计，请稍后重试。",
                true);
        }

        if (exception is AssistantMessageValidationException)
        {
            return new AssistantErrorResponse(
                "invalid_message",
                "请填写消息内容，并控制在 8000 字以内后再发送。",
                false);
        }

        if (exception is TimeoutException or OperationCanceledException)
        {
            return new AssistantErrorResponse(
                "ai_timeout",
                "这次回答等待时间过长，请重新发送一次。",
                true);
        }

        return new AssistantErrorResponse(
            "ai_unavailable",
            "暂时没能完成这次回答，请稍后重试。",
            true);
    }

    private static bool ContainsMalformedStreamingProtocol(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is IncompleteAiResponseException ||
                current.Message.Contains("Unknown ChatFinishReason", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("Unknown ChatToolCallKind", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsStatisticsToolError(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AssistantStatisticsToolException) return true;
        }

        return false;
    }
}
