using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Ai;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Ai.Amap;
using PassingTrace.Events.Api.Ai.Capabilities;
using PassingTrace.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

/// <summary>Exercises the actual SSE service with real PostgreSQL and MCP, but no paid model/embedding/map calls.</summary>
public sealed class AssistantSkillServiceTests(StorylinePostgresFixture fixture) : IClassFixture<StorylinePostgresFixture>
{
    [Theory]
    [InlineData("你好", null, "你好，今天想聊点什么？")]
    [InlineData("今天跑了五公里，真开心", "conversation", "听起来很有成就感！")]
    [InlineData("把刚才聊的整理一下", "conversation-summary", "根据刚才的讨论，你已跑完步，明天想去公园。")]
    [InlineData("把这些安排整理为明天的计划", "planning", "这是尚未保存的草稿：明天去公园，时间待确认。")]
    [InlineData("附近有什么餐厅", "amap", "你希望查哪个位置附近的餐厅？")]
    public async Task Normal_reply_or_clarification_survives_without_background_search(string question, string? skill, string answer)
    {
        var probe = new QueryProbe();
        await using var db = new TraceDbContext(new DbContextOptionsBuilder<TraceDbContext>(fixture.Options)
            .AddInterceptors(probe).Options);
        const long userId = 971001;
        var user = new CurrentUserContext(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")),
            },
        });
        var now = DateTimeOffset.UtcNow;
        var conversation = new AiConversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = "场景测试",
            CreatedAt = now,
            UpdatedAt = now,
        };
        conversation.Messages.Add(new AiMessage
        {
            UserId = userId,
            Role = AiMessageRole.User,
            Content = "我刚跑完步，明天想去公园。",
            CreatedAt = now,
        });
        db.AiConversations.Add(conversation);
        await db.SaveChangesAsync();
        probe.Commands.Clear();
        var personal = new PersonalRecordTools(db, user, new NoEmbedding());
        var maps = new AmapAiTools(new NoMaps(), new NoQuota(), TimeProvider.System);
        using var model = new ScenarioModel(question, skill, answer);
        // Only cache reads are allowed. A text-only answer must never be written to the 24-hour evidence cache.
        var cache = StrictProxy.Create<IDatabase>(method => method.Name == "StringGetAsync"
            ? Task.FromResult(RedisValue.Null) : throw new InvalidOperationException("Unexpected cache write."));
        var redis = StrictProxy.Create<IConnectionMultiplexer>(method => method.Name == "GetDatabase"
            ? cache : throw new InvalidOperationException("Unexpected Redis operation."));
        using var services = new ServiceCollection().BuildServiceProvider();
        var service = new AssistantService(db, user, personal, maps, [new PersonalRecordsCapabilityPackage(personal)],
            model, redis, Options.Create(new AiModelOptions()), NullLoggerFactory.Instance, services, TimeProvider.System);

        var stream = new List<AssistantStreamEvent>();
        await foreach (var item in service.SendAsync(conversation.Id, question, default)) stream.Add(item);

        Assert.Equal("done", stream[^1].Type);
        Assert.DoesNotContain(stream, item => item.Type == "action");
        var evidence = Assert.IsType<EvidenceBundle>(Assert.Single(stream, item => item.Type == "evidence").Data);
        Assert.Empty(evidence.Records);
        Assert.Empty(evidence.Memories);
        var saved = await db.AiMessages.SingleAsync(message => message.ConversationId == conversation.Id && message.Role == AiMessageRole.Assistant);
        Assert.Equal(answer, saved.Content);
        Assert.Contains("/skills:", saved.PromptVersion);
        Assert.Equal(skill is null ? 1 : 2, model.Calls);
        Assert.All(probe.Commands, sql =>
        {
            Assert.DoesNotContain("trace_event", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user_memory", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storyline_search_index", sql, StringComparison.OrdinalIgnoreCase);
        });
    }

    public class StrictProxy : DispatchProxy
    {
        public Func<MethodInfo, object?> Handler { get; set; } = null!;
        public static T Create<T>(Func<MethodInfo, object?> handler) where T : class
        {
            var instance = Create<T, StrictProxy>();
            ((StrictProxy)(object)instance).Handler = handler;
            return instance;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!);
    }

    private sealed class ScenarioModel(string question, string? skill, string answer) : IChatClient
    {
        public int Calls { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No extra title/summary call expected.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.True(Calls <= 2);
            Assert.Equal(question, messages.Last(message => message.Role == ChatRole.User).Text);
            Assert.Contains(options!.Tools!, tool => tool.Name == "ReadAssistantSkill");
            if (Calls == 1 && skill is not null)
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new FunctionCallContent("skill-1", "ReadAssistantSkill", new Dictionary<string, object?> { ["key"] = skill })],
                    FinishReason = ChatFinishReason.ToolCalls,
                };
            else
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(answer)],
                    FinishReason = ChatFinishReason.Stop,
                };
            await Task.CompletedTask;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class NoEmbedding : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Text-only turns must not request embeddings.");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
    private sealed class NoMaps : IAmapMcpGateway
    {
        public bool IsConfigured => false;
        public Task<AmapMcpResponse> CallAsync(IReadOnlyList<string> toolNames, IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken) => throw new InvalidOperationException("No location or external request was authorized.");
    }
    private sealed class NoQuota : IAmapQuotaGuard
    {
        public Task<bool> TryConsumeAsync(AmapQuotaKind kind, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
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
}
