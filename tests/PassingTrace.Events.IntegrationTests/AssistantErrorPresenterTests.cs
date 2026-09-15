using PassingTrace.Events.Api.Ai;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantErrorPresenterTests
{
    [Fact]
    public void MessageValidation_ReturnsActionableInputHint()
    {
        var result = AssistantErrorPresenter.Present(new AssistantMessageValidationException());

        Assert.Equal("invalid_message", result.Code);
        Assert.Equal("请填写消息内容，并控制在 8000 字以内后再发送。", result.Message);
        Assert.False(result.Retryable);
    }

    [Theory]
    [InlineData("metric")]
    [InlineData("content")]
    public void InternalArgumentError_DoesNotBlameUserInput(string parameter)
    {
        var exception = new ArgumentOutOfRangeException(parameter, "Unsupported internal tool option.");

        var result = AssistantErrorPresenter.Present(exception);

        Assert.Equal("ai_unavailable", result.Code);
        Assert.Equal("暂时没能完成这次回答，请稍后重试。", result.Message);
        Assert.True(result.Retryable);
        Assert.DoesNotContain(parameter, result.Message);
        Assert.DoesNotContain("检查内容", result.Message);
    }

    [Fact]
    public void WrappedToolArgumentError_ReturnsFriendlyRetryableMessage()
    {
        var result = AssistantErrorPresenter.Present(new InvalidOperationException(
            "Tool invocation failed.", new ArgumentException("Internal tool details.", "metric")));

        Assert.Equal("ai_unavailable", result.Code);
        Assert.True(result.Retryable);
        Assert.DoesNotContain("metric", result.Message);
        Assert.DoesNotContain("检查内容", result.Message);
    }

    [Fact]
    public void StatisticsToolError_DoesNotAskUserToChangeTheirQuestion()
    {
        var result = AssistantErrorPresenter.Present(new AssistantStatisticsToolException());

        Assert.Equal("statistics_unavailable", result.Code);
        Assert.Equal("暂时没能完成消费或记录统计，请稍后重试。", result.Message);
        Assert.True(result.Retryable);
        Assert.DoesNotContain("检查内容", result.Message);
    }

    [Fact]
    public void WrappedStatisticsToolError_StillReturnsStatisticsHint()
    {
        var result = AssistantErrorPresenter.Present(new InvalidOperationException(
            "Provider tool wrapper.", new AssistantStatisticsToolException()));

        Assert.Equal("statistics_unavailable", result.Code);
        Assert.True(result.Retryable);
        Assert.DoesNotContain("Provider", result.Message);
    }

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
    public void UnknownToolCallKind_ReturnsFriendlyRetryableMessage()
    {
        var exception = new InvalidOperationException(
            "AI stream failed.",
            new ArgumentOutOfRangeException("value", "", "Unknown ChatToolCallKind value."));

        var result = AssistantErrorPresenter.Present(exception);

        Assert.Equal("incomplete_ai_response", result.Code);
        Assert.Equal("AI 服务刚才返回了不完整结果，请重新发送一次。", result.Message);
        Assert.True(result.Retryable);
        Assert.DoesNotContain("ChatToolCallKind", result.Message);
        Assert.DoesNotContain("Parameter", result.Message);
    }

    [Fact]
    public void StreamWithoutTerminalChunk_ReturnsFriendlyRetryableMessage()
    {
        var result = AssistantErrorPresenter.Present(new IncompleteAiResponseException());

        Assert.Equal("incomplete_ai_response", result.Code);
        Assert.True(result.Retryable);
        Assert.DoesNotContain("stream", result.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Internal_mcp_failure_is_shown_as_plain_language_without_protocol_details()
    {
        var result = AssistantErrorPresenter.Present(new InvalidOperationException(
            "MCP protocol tool error: secret argument", new AssistantToolInvocationException()));
        Assert.Equal("record_tools_unavailable", result.Code);
        Assert.Equal("暂时没能完成这次记录查询，请稍后重试。", result.Message);
        Assert.DoesNotContain("MCP", result.Message);
        Assert.DoesNotContain("secret", result.Message);
    }
}
