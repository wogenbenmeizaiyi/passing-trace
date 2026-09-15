using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantConversationContextProviderTests
{
    [Fact]
    public async Task Long_conversation_keeps_the_current_question_as_the_last_model_message()
    {
        var snapshot = new ConversationContextSnapshot(
            "此前讨论过运动、饮食和旅行记录。",
            20,
            [
                new ConversationContextMessage(21, AiMessageRole.User, "这个月吃了什么"),
                new ConversationContextMessage(22, AiMessageRole.Assistant, "列出了本月饮食记录。"),
            ],
            []);
        using var client = new CapturingChatClient();
        var agent = new ChatClientAgent(client);
        var session = await agent.CreateSessionAsync();

        await agent.RunAsync(snapshot.BuildPromptMessages("我的旅游打卡点有哪些？"), session);

        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        Assert.Contains("历史会话摘要", client.Messages[0].Text);
        Assert.Equal("这个月吃了什么", client.Messages[1].Text);
        Assert.Equal("列出了本月饮食记录。", client.Messages[2].Text);
        Assert.Equal(ChatRole.User, client.Messages[^1].Role);
        Assert.Equal("我的旅游打卡点有哪些？", client.Messages[^1].Text);
        Assert.Single(client.Messages, message => message.Text == "我的旅游打卡点有哪些？");
    }

    [Fact]
    public async Task Current_question_remains_last_when_recent_map_candidates_are_present()
    {
        var snapshot = new ConversationContextSnapshot(
            "",
            0,
            [new ConversationContextMessage(1, AiMessageRole.Assistant, "找到了两个地点。")],
            [new AmapPlaceEvidence("candidate-1", null, "人民广场", null, null, "上海市", null,
                31.2304m, 121.4737m, "GCJ02", "amap-live")]);
        using var client = new CapturingChatClient();
        var agent = new ChatClientAgent(client);
        var session = await agent.CreateSessionAsync();

        await agent.RunAsync(snapshot.BuildPromptMessages("导航到第二个"), session);

        Assert.Equal(ChatRole.System, client.Messages[0].Role);
        Assert.Contains("candidate-1", client.Messages[0].Text);
        Assert.Equal(ChatRole.User, client.Messages[^1].Role);
        Assert.Equal("导航到第二个", client.Messages[^1].Text);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "回答")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
