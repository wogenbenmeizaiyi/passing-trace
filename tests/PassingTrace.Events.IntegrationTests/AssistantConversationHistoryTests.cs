using System.Data.Common;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Infrastructure;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AssistantConversationHistoryTests(StorylinePostgresFixture fixture)
    : IClassFixture<StorylinePostgresFixture>, IAsyncLifetime
{
    private readonly QueryProbe _probe = new();
    private TraceDbContext _db = null!;

    public Task InitializeAsync()
    {
        _db = new TraceDbContext(new DbContextOptionsBuilder<TraceDbContext>(fixture.Options)
            .AddInterceptors(_probe).Options);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task History_pages_only_owned_metadata_and_keeps_equal_timestamp_cursor_stable()
    {
        const long userId = 95_001;
        var now = new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
        for (var index = 1; index <= 5; index++)
        {
            var conversation = Conversation(userId, now, Guid.Parse($"00000000-0000-0000-0000-{index:D12}"));
            conversation.Messages.Add(Message(userId, "private-body", now));
            _db.AiConversations.Add(conversation);
        }
        _db.AiConversations.Add(Conversation(userId + 1, now.AddHours(1)));
        var deleted = Conversation(userId, now.AddHours(1));
        deleted.DeletedAt = now;
        _db.AiConversations.Add(deleted);
        await _db.SaveChangesAsync();
        _probe.Commands.Clear();

        var first = await AssistantConversationHistory.ListPageAsync(_db, userId, 2, null, default);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var second = await AssistantConversationHistory.ListPageAsync(_db, userId, 2, first.NextCursor, default);
        var third = await AssistantConversationHistory.ListPageAsync(_db, userId, 2, second.NextCursor, default);
        var rows = first.Items.Concat(second.Items).Concat(third.Items).ToArray();
        Assert.Equal(5, rows.Length);
        Assert.Equal(5, rows.Select(x => x.Id).Distinct().Count());
        Assert.Null(third.NextCursor);
        Assert.Equal("00000000-0000-0000-0000-000000000005", first.Items[0].Id.ToString());
        Assert.All(_probe.Commands, sql =>
        {
            Assert.DoesNotContain("ai_message", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("evidence_snapshot", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        });
        Assert.DoesNotContain("private-body", JsonSerializer.Serialize(first));
    }

    [Fact]
    public async Task Older_messages_load_only_on_request_and_each_page_is_chronological()
    {
        const long userId = 95_011;
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation(userId, now);
        for (var index = 0; index < 65; index++)
            conversation.Messages.Add(Message(userId, $"message-{index}", now.AddMinutes(index)));
        _db.AiConversations.Add(conversation);
        await _db.SaveChangesAsync();

        var first = await AssistantConversationHistory.GetMessagesPageAsync(_db, userId, conversation.Id, 30, null, default);
        Assert.NotNull(first);
        Assert.Equal(30, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Equal("message-35", first.Items[0].Content);
        Assert.Equal("message-64", first.Items[^1].Content);
        var second = await AssistantConversationHistory.GetMessagesPageAsync(_db, userId, conversation.Id, 30, first.NextBeforeId, default);
        var third = await AssistantConversationHistory.GetMessagesPageAsync(_db, userId, conversation.Id, 30, second!.NextBeforeId, default);
        Assert.NotNull(third);
        Assert.Equal(5, third.Items.Count);
        Assert.False(third.HasMore);
        Assert.Null(third.NextBeforeId);
        Assert.Equal(65, first.Items.Concat(second.Items).Concat(third.Items).Select(x => x.Id).Distinct().Count());
        Assert.Null(await AssistantConversationHistory.GetMessagesPageAsync(_db, userId + 1, conversation.Id, 30, null, default));
        Assert.Null(await AssistantConversationHistory.GetSummaryAsync(_db, userId + 1, conversation.Id, default));
        conversation.DeletedAt = now;
        await _db.SaveChangesAsync();
        Assert.Null(await AssistantConversationHistory.GetMessagesPageAsync(_db, userId, conversation.Id, 30, null, default));
    }

    [Fact]
    public async Task Page_size_is_bounded_and_invalid_cursor_rejected()
    {
        const long userId = 95_021;
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 55; index++)
            _db.AiConversations.Add(Conversation(userId, now));
        await _db.SaveChangesAsync();
        var page = await AssistantConversationHistory.ListPageAsync(_db, userId, 100_000, null, default);
        Assert.Equal(50, page.Items.Count);
        Assert.NotNull(page.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            AssistantConversationHistory.ListPageAsync(_db, userId, 20, "not-a-cursor", default));
    }

    [Fact]
    public async Task First_answer_generates_topic_once_and_later_answers_preserve_it()
    {
        const long userId = 95_031;
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation(userId, now);
        conversation.Messages.Add(Message(userId, "帮我统计这个月的消费金额", now));
        _db.AiConversations.Add(conversation);
        await _db.SaveChangesAsync();
        var client = new TitleClient("本月消费金额汇总");
        var service = CreateService(userId, client);
        await SaveAnswer(service, conversation, "这个月合计120元，来自两条记录。");
        Assert.Equal("本月消费金额汇总", conversation.Title);
        await SaveAnswer(service, conversation, "后来聊到了运动量。");
        Assert.Equal(1, client.Calls);
        Assert.Equal("本月消费金额汇总", (await _db.AiConversations.AsNoTracking().SingleAsync(x => x.Id == conversation.Id)).Title);
        Assert.Contains("这个月合计120元", client.Prompt);
        Assert.Contains("统计这个月的消费金额", client.Prompt);
        Assert.Empty(client.Options!.Tools!);
        Assert.Equal(ChatToolMode.None, client.Options.ToolMode);
        Assert.Equal(64, client.Options.MaxOutputTokens);
    }

    [Fact]
    public async Task Title_failure_keeps_saved_answer_and_never_retries_after_next_reply()
    {
        const long userId = 95_041;
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation(userId, now);
        conversation.Messages.Add(Message(userId, "请帮我看看最近的运动记录", now));
        _db.AiConversations.Add(conversation);
        await _db.SaveChangesAsync();
        var client = new TitleClient(null);
        var service = CreateService(userId, client);
        await SaveAnswer(service, conversation, "最近跑了三次步。");
        Assert.Equal("看看最近的运动记录", conversation.Title);
        await SaveAnswer(service, conversation, "三次都在公园。");
        Assert.Equal(1, client.Calls);
        Assert.Equal(2, await _db.AiMessages.CountAsync(x => x.ConversationId == conversation.Id && x.Role == AiMessageRole.Assistant));
    }

    [Fact]
    public async Task Explicit_title_is_not_replaced_or_sent_to_title_model()
    {
        const long userId = 95_051;
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation(userId, now);
        conversation.Title = "我的旅行计划";
        conversation.Messages.Add(Message(userId, "安排周末", now));
        _db.AiConversations.Add(conversation);
        await _db.SaveChangesAsync();
        var client = new TitleClient("不应使用");
        await SaveAnswer(CreateService(userId, client), conversation, "可以周末去爬山。");
        Assert.Equal(0, client.Calls);
        Assert.Equal("我的旅行计划", conversation.Title);
    }

    [Fact]
    public async Task First_successful_answer_uses_its_question_after_an_earlier_request_failed()
    {
        const long userId = 95_061;
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation(userId, now);
        conversation.Messages.Add(Message(userId, "统计这个月的消费金额", now));
        _db.AiConversations.Add(conversation);
        await _db.SaveChangesAsync();
        // 第一条请求失败，没有保存 Assistant 回答；用户随后换了一个问题。
        conversation.Messages.Add(Message(userId, "回顾最近的运动记录", now.AddMinutes(1)));
        await _db.SaveChangesAsync();

        var client = new TitleClient("近期运动情况回顾");
        await SaveAnswer(CreateService(userId, client), conversation, "最近完成了三次公园跑步。");

        Assert.Equal("近期运动情况回顾", conversation.Title);
        Assert.Contains("回顾最近的运动记录", client.Prompt);
        Assert.Contains("最近完成了三次公园跑步", client.Prompt);
        Assert.DoesNotContain("统计这个月的消费金额", client.Prompt);
        Assert.Equal(1, client.Calls);
    }

    private AssistantService CreateService(long userId, IChatClient client)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")),
            }
        };
        return new AssistantService(_db, new CurrentUserContext(accessor), null!, null!, [], client,
            null!, Options.Create(new AiModelOptions()), NullLoggerFactory.Instance,
            new ServiceCollection().BuildServiceProvider(), TimeProvider.System);
    }

    private static Task SaveAnswer(AssistantService service, AiConversation conversation, string answer) =>
        (Task)typeof(AssistantService).GetMethod("SaveAssistantAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [conversation, answer, new EvidenceBundle([], []), 0L, CancellationToken.None])!;

    private static AiConversation Conversation(long userId, DateTimeOffset now, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        UserId = userId,
        Title = AssistantConversationTitle.DefaultTitle,
        CreatedAt = now,
        UpdatedAt = now,
    };

    private static AiMessage Message(long userId, string content, DateTimeOffset now) => new()
    {
        UserId = userId,
        Role = AiMessageRole.User,
        Content = content,
        CreatedAt = now,
        EvidenceSnapshotJson = "{\"records\":[]}",
    };

    private sealed class QueryProbe : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class TitleClient(string? answer) : IChatClient
    {
        public int Calls { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public ChatOptions? Options { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Prompt = string.Join('\n', messages.Select(x => x.Text));
            Options = options;
            return answer is null ? Task.FromException<ChatResponse>(new InvalidOperationException("title unavailable")) :
                Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
