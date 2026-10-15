using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai.Assistant.Context;
using PassingTrace.Events.Api.Ai.Tools.Mutations;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class MutationIntentPolicyTests
{
    [Theory]
    [InlineData("加上")]
    [InlineData("先不用删，你先加")]
    [InlineData("不要删除旧计划，帮我创建故事线")]
    public void Explicit_add_is_not_canceled_by_a_separate_do_not_delete_clause(string question) =>
        Assert.True(PersonalMutationTools.MayWrite(question));

    [Theory]
    [InlineData("对就这样")]
    [InlineData("确定")]
    [InlineData("就按这个来")]
    [InlineData("好的，按刚才方案来")]
    public void Confirmation_requires_the_users_request_and_a_pending_assistant_proposal(string question)
    {
        ConversationContextMessage[] history = [
            new(1, AiMessageRole.User, "帮我创建故事线，拆成7个计划"),
            new(2, AiMessageRole.Assistant, "确认这7个环节可以吗？你回一句我就建故事线。"),
        ];
        Assert.True(PersonalMutationTools.MayWrite(question, history));
        Assert.False(PersonalMutationTools.MayWrite(question));
        Assert.False(PersonalMutationTools.MayWrite(question, history.Skip(1).ToArray()));
    }

    [Theory]
    [InlineData("别加上")]
    [InlineData("不用追加计划")]
    [InlineData("先不要保存")]
    [InlineData("取消创建")]
    [InlineData("总结一下")]
    [InlineData("我今天加上了一个计划")]
    public void Negations_and_ordinary_messages_cannot_inherit_write_permission(string question) =>
        Assert.False(PersonalMutationTools.MayWrite(question, PendingCreate()));

    [Fact]
    public void Assistant_suggestion_after_ordinary_chat_is_not_a_write_request()
    {
        ConversationContextMessage[] history = [
            new(1, AiMessageRole.User, "总结刚才的行程"),
            new(2, AiMessageRole.Assistant, "要不要帮你创建故事线？"),
        ];
        Assert.False(PersonalMutationTools.MayWrite("确定", history));
    }

    [Fact]
    public void Canceled_request_or_changed_topic_cannot_be_reactivated_by_confirmation()
    {
        Assert.False(PersonalMutationTools.MayWrite("确定", [.. PendingCreate(), new(3, AiMessageRole.User, "不用保存了")]));
        Assert.False(PersonalMutationTools.MayWrite("确定", [.. PendingCreate(), new(3, AiMessageRole.User, "明天天气怎么样")]));
        Assert.False(PersonalMutationTools.MayWrite("确定", [.. PendingCreate(), new(3, AiMessageRole.Assistant, "明天晴天。")]));
    }

    [Fact]
    public void Completed_receipt_cannot_be_reused_as_a_pending_proposal() =>
        Assert.False(PersonalMutationTools.MayWrite("确定", [.. PendingCreate(),
            new(3, AiMessageRole.Assistant, "已创建故事线；确认后还可以修改。", IsMutationReceipt: true)]));

    [Fact]
    public void Final_answer_after_a_receipt_cannot_reactivate_the_completed_request() =>
        Assert.False(PersonalMutationTools.MayWrite("确定", [.. PendingCreate(),
            new(3, AiMessageRole.Assistant, "已创建故事线。", IsMutationReceipt: true),
            new(4, AiMessageRole.Assistant, "故事线已经创建，请确认能够打开链接。") ]));

    [Fact]
    public void Delete_confirmation_never_inherits_a_delete_request()
    {
        ConversationContextMessage[] history = [
            new(1, AiMessageRole.User, "删除旧计划"),
            new(2, AiMessageRole.Assistant, "确定要删除吗？"),
        ];
        Assert.False(PersonalMutationTools.MayWrite("确定", history));
    }

    private static ConversationContextMessage[] PendingCreate() => [
        new(1, AiMessageRole.User, "帮我创建故事线"),
        new(2, AiMessageRole.Assistant, "确认要按这个方案创建故事线吗？"),
    ];
}
