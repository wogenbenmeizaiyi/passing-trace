using PassingTrace.Events.Api.Ai;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantErrorPresenterTests
{
    [Fact]
    public void UnknownFinishReason_ReturnsFriendlyRetryableMessage()
    {
        var exception = new InvalidOperationException(
            "AI stream failed.",
            new ArgumentOutOfRangeException("value", "Unknown ChatFinishReason value."));

        var result = AssistantErrorPresenter.Present(exception);

        Assert.Equal("incomplete_ai_response", result.Code);
        Assert.Equal("AI 服务刚才返回了不完整结果，请重新发送一次。", result.Message);
        Assert.True(result.Retryable);
        Assert.DoesNotContain("ChatFinishReason", result.Message);
        Assert.DoesNotContain("Parameter", result.Message);
    }

    [Fact]
    public void UnexpectedException_DoesNotExposeInternalMessage()
    {
        const string sensitiveDetail = "Connection failed at localhost:5432 with secret-token.";

        var result = AssistantErrorPresenter.Present(new InvalidOperationException(sensitiveDetail));

        Assert.Equal("ai_unavailable", result.Code);
        Assert.Equal("暂时没能完成这次回答，请稍后重试。", result.Message);
        Assert.True(result.Retryable);
        Assert.DoesNotContain(sensitiveDetail, result.Message);
    }
}
