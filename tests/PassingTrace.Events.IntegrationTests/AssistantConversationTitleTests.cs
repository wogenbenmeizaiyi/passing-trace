using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantConversationTitleTests
{
    [Theory]
    [InlineData("**本月消费** [Event #12]\n汇总", "本月消费 汇总")]
    [InlineData("[公园晨跑](https://passingtrace.com/events/1)", "公园晨跑")]
    [InlineData("\n\t", "新的对话")]
    public void Historical_display_removes_markdown_without_requesting_model(string value, string expected) =>
        Assert.Equal(expected, AssistantConversationTitle.CleanDisplay(value));

    [Theory]
    [InlineData("标题：本月消费金额汇总", "本月消费金额汇总")]
    [InlineData("**最近运动情况回顾**", "最近运动情况回顾")]
    [InlineData("抱歉，无法生成标题", "统计本月消费")]
    [InlineData("这是一个远远超出标题长度限制的完整回答因此不应该被当成主题标题", "统计本月消费")]
    public async Task Generated_title_is_compact_plain_text_or_safe_question_fallback(string response, string expected)
    {
        var title = await AssistantConversationTitle.GenerateAsync(new FixedTitleClient(response),
            "请帮我统计本月消费", "已找到三条消费记录，合计150元。", default);
        Assert.Equal(expected, title);
    }

    [Fact]
    public async Task Cancelled_title_generation_does_not_swallow_request_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AssistantConversationTitle.GenerateAsync(
            new FixedTitleClient("本月消费金额汇总"), "本月消费", "150元", cancellation.Token));
    }

    private sealed class FixedTitleClient(string response) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
