using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantCompletionGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("stop")]
    [InlineData("tool_calls")]
    public void ContinuingSuccessfulOrToolResponse_IsUnchanged(string? reason)
    {
        var update = new AgentResponseUpdate { FinishReason = reason is null ? null : new ChatFinishReason(reason) };

        AssistantCompletionGuard.ThrowIfUnsuccessful(update.FinishReason);

        Assert.Equal(reason, update.FinishReason?.Value);
    }

    [Fact]
    public void LengthLimitedAnswer_IsRejectedWithActionableMessage()
    {
        var update = new AgentResponseUpdate { FinishReason = ChatFinishReason.Length };

        var error = Assert.Throws<AssistantResponseLimitException>(() => AssistantCompletionGuard.ThrowIfUnsuccessful(update.FinishReason));
        var presented = AssistantErrorPresenter.Present(new InvalidOperationException("SDK wrapper", error));

        Assert.Equal("ai_response_too_long", presented.Code);
        Assert.Contains("缩小范围", presented.Message);
        Assert.True(presented.Retryable);
    }

    [Fact]
    public void ProviderBlockedAnswer_IsRejectedWithoutClaimingItSucceeded()
    {
        var update = new AgentResponseUpdate { FinishReason = ChatFinishReason.ContentFilter };

        var error = Assert.Throws<AssistantResponseBlockedException>(() => AssistantCompletionGuard.ThrowIfUnsuccessful(update.FinishReason));
        var presented = AssistantErrorPresenter.Present(new InvalidOperationException("SDK wrapper", error));

        Assert.Equal("ai_response_blocked", presented.Code);
        Assert.Contains("AI 服务", presented.Message);
        Assert.False(presented.Retryable);
        Assert.DoesNotContain("ContentFilter", presented.Message);
    }
}
