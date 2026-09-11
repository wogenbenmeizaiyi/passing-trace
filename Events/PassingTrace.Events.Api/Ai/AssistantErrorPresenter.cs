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

        if (ContainsUnknownFinishReason(exception))
        {
            return new AssistantErrorResponse(
                "incomplete_ai_response",
                "AI 服务刚才返回了不完整结果，请重新发送一次。",
                true);
        }

        if (exception is ArgumentException)
        {
            return new AssistantErrorResponse(
                "invalid_message",
                "这条消息暂时无法发送，请检查内容后再试。",
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

    private static bool ContainsUnknownFinishReason(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Unknown ChatFinishReason", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
