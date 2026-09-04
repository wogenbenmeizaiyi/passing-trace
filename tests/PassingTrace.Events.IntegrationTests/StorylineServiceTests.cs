using Microsoft.EntityFrameworkCore;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Storylines;
using PassingTrace.Infrastructure;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class StorylinePostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18-trixie")
        .WithDatabase("storyline_tests")
        .WithUsername("postgres")
        .WithPassword("passingtrace-test-postgres")
        .Build();

    public DbContextOptions<TraceDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Options = new DbContextOptionsBuilder<TraceDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
            {
                npgsql.UseVector();
                npgsql.EnableRetryOnFailure();
            })
            .Options;
        await using var db = new TraceDbContext(Options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

public sealed class StorylineServiceTests : IClassFixture<StorylinePostgresFixture>, IAsyncLifetime
{
    private readonly StorylinePostgresFixture _fixture;
    private TraceDbContext _db = null!;
    private StorylineService _service = null!;

    public StorylineServiceTests(StorylinePostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        _db = new TraceDbContext(_fixture.Options);
        _service = new StorylineService(_db, new AnalysisOutbox(_db), TimeProvider.System);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Create_supports_branch_and_creates_inline_plan_atomically()
    {
        var eventA = AddEvent(7, "买票");
        var eventB = AddEvent(7, "抵达山脚");
        await _db.SaveChangesAsync();
        var stage = Guid.NewGuid();
        var start = Guid.NewGuid();
        var branch = Guid.NewGuid();
        var plan = Guid.NewGuid();

        var result = await _service.CreateAsync(7, new SaveStorylineRequest(
            "黄山旅行", "把沿途记录串起来", "trip", StorylineStatus.Ongoing, null, ["登山"],
            [new(stage, "出发", 0)],
            [
                Existing(start, eventA.Id, 1, stage, 0),
                Existing(branch, eventB.Id, 1, stage, 1),
                new(plan, "new-plan", null, null, new("看日出", null, "提前查天气", "Asia/Shanghai"),
                    stage, 2, StorylineNodeEmphasis.Important),
            ],
            [
                new(Guid.NewGuid(), start, branch, StorylineRelationType.Sequence, null),
                new(Guid.NewGuid(), start, plan, StorylineRelationType.Branch, "天气好时"),
            ],
            null), "create-trip", default);

        Assert.Equal(3, result.Storyline.Nodes.Count);
        Assert.True(result.CreatedPlans.ContainsKey(plan));
        var createdPlanId = result.CreatedPlans[plan];
        Assert.Equal(EventKind.Plan, (await _db.Events.FindAsync(createdPlanId))!.EventKind);
        Assert.Single(await _db.StorylineRevisions
            .Where(x => x.StorylineId == result.Storyline.Id).ToListAsync());
        Assert.Equal(2, await _db.StorylineEdges
            .CountAsync(x => x.Revision.StorylineId == result.Storyline.Id));
        Assert.Contains(await _db.OutboxMessages.ToListAsync(), x => x.MessageType == "storyline.index");
    }

    [Fact]
    public async Task Idempotent_retry_returns_the_same_inline_plan_mapping()
    {
        var node = Guid.NewGuid();
        var request = new SaveStorylineRequest("重试故事", null, "activity", StorylineStatus.Ongoing, null, [], [],
            [new(node, "new-plan", null, null, new("集合出发", null, null, "Asia/Shanghai"), null, 0)],
            [], null);

        var first = await _service.CreateAsync(17, request, "retry-story", default);
        _db.ChangeTracker.Clear();
        var second = await _service.CreateAsync(17, request, "retry-story", default);

        Assert.Equal(first.Storyline.Id, second.Storyline.Id);
        Assert.Equal(first.CreatedPlans[node], second.CreatedPlans[node]);
        Assert.Equal(1, await _db.Events.CountAsync(x => x.UserId == 17 && x.Title == "集合出发"));
        Assert.Equal(1, await _db.StorylineRevisions.CountAsync(x => x.StorylineId == first.Storyline.Id));
    }

    [Fact]
    public async Task Cycle_rejects_graph_and_rolls_back_inline_plan()
    {
        var eventA = AddEvent(8, "第一步");
        await _db.SaveChangesAsync();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var before = await _db.Events.CountAsync();
        var request = new SaveStorylineRequest("循环", null, "project", StorylineStatus.Ongoing, null, [], [],
            [
                Existing(a, eventA.Id, 1, null, 0),
                new(b, "new-plan", null, null, new("第二步", null, null, "UTC"), null, 1),
            ],
            [
                new(Guid.NewGuid(), a, b, StorylineRelationType.Sequence, null),
                new(Guid.NewGuid(), b, a, StorylineRelationType.Sequence, null),
            ], null);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            _service.CreateAsync(8, request, "cycle", default));

        Assert.Equal(before, await _db.Events.CountAsync());
        Assert.DoesNotContain(await _db.Storylines.ToListAsync(), x => x.Title == "循环");
        Assert.DoesNotContain(await _db.Events.ToListAsync(), x => x.Title == "第二步");
    }

    [Fact]
    public async Task Cross_user_event_is_never_accepted()
    {
        var foreign = AddEvent(99, "别人的记录");
        await _db.SaveChangesAsync();
        var request = new SaveStorylineRequest("越权", null, "other", StorylineStatus.Ongoing, null, [], [],
            [Existing(Guid.NewGuid(), foreign.Id, 1, null, 0)], [], null);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            _service.CreateAsync(1, request, "cross-user", default));
    }

    [Fact]
    public async Task Automatic_cover_uses_confirmed_original_when_ai_processing_failed()
    {
        var evt = AddEvent(31, "有照片的记录");
        var now = DateTimeOffset.UtcNow;
        var image = new MediaAsset
        {
            Id = Guid.NewGuid(),
            UserId = 31,
            ObjectKey = "users/31/photo.jpg",
            OriginalFileName = "photo.jpg",
            Kind = MediaKind.Image,
            DeclaredMimeType = "image/jpeg",
            VerifiedMimeType = "image/jpeg",
            ExpectedSize = 128,
            ActualSize = 128,
            ExpectedSha256 = new string('a', 64),
            ActualSha256 = new string('a', 64),
            Status = MediaAssetStatus.Failed,
            UploadMode = MediaUploadMode.Single,
            UploadExpiresAt = now.AddHours(1),
            ConfirmedAt = now,
            ProcessingError = "thumbnail generation failed",
            CreatedAt = now,
            UpdatedAt = now,
        };
        evt.SourceRevisions.Single().MediaAssets.Add(new SourceRevisionMedia
        {
            MediaAsset = image,
            MediaAssetId = image.Id,
            SortOrder = 0,
        });
        await _db.SaveChangesAsync();

        var result = await _service.CreateAsync(31, new SaveStorylineRequest(
            "照片故事", null, "activity", StorylineStatus.Ongoing, null, [], [],
            [Existing(Guid.NewGuid(), evt.Id, 1, null, 0)], [], null), "failed-image-cover", default);

        Assert.Equal(image.Id, result.Storyline.CoverMediaAssetId);
        _db.ChangeTracker.Clear();
        var summaries = await _service.ListAsync(31, null, null, null, null, 20, null, default);
        Assert.Equal(image.Id, Assert.Single(summaries).CoverMediaAssetId);
    }

    [Fact]
    public async Task Mobile_add_keeps_old_coordinates_and_marks_new_node_for_arrangement()
    {
        var first = AddEvent(12, "开始");
        var second = AddEvent(12, "后来");
        await _db.SaveChangesAsync();
        var key = Guid.NewGuid();
        var created = await _service.CreateAsync(12, new SaveStorylineRequest("项目", null, "project",
            StorylineStatus.Ongoing, null, [], [], [Existing(key, first.Id, 1, null, 0)], [],
            new("LR", 0, 0, 1, [new(key, 40, 60, 260, 150)], [])), "layout", default);

        var changed = await _service.ApplyChangeAsync(12, created.Storyline.Id, created.Storyline.Version,
            new("add-existing-event", NodeKey: Guid.NewGuid(), EventId: second.Id, ParentNodeKey: key),
            "mobile-add", default);

        Assert.Equal(StorylineLayoutState.NeedsArrangement, changed.Storyline.LayoutState);
        var oldLayout = changed.Storyline.WebCanvasLayout!.Nodes!.Single(x => x.NodeKey == key);
        Assert.Equal(40, oldLayout.X);
        Assert.Equal(60, oldLayout.Y);
    }

    [Fact]
    public async Task Conversation_context_keeps_only_messages_after_the_summary_watermark()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new AiConversation
        {
            Id = Guid.NewGuid(),
            UserId = 501,
            Title = "上下文测试",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var first = new AiMessage
        {
            Conversation = conversation,
            UserId = 501,
            Role = AiMessageRole.User,
            Content = "我上一个问题是什么？",
            CreatedAt = now,
            ExpiresAt = now.AddDays(1),
        };
        var firstAnswer = new AiMessage
        {
            Conversation = conversation,
            UserId = 501,
            Role = AiMessageRole.Assistant,
            Content = "你问了上一个问题。",
            CreatedAt = now.AddSeconds(1),
            ExpiresAt = now.AddDays(1),
        };
        var followUp = new AiMessage
        {
            Conversation = conversation,
            UserId = 501,
            Role = AiMessageRole.User,
            Content = "那我为什么会重复问？",
            CreatedAt = now.AddSeconds(2),
            ExpiresAt = now.AddDays(1),
        };
        conversation.Messages.AddRange([first, firstAnswer, followUp]);
        _db.AiConversations.Add(conversation);
        await _db.SaveChangesAsync();
        _db.ConversationSummaries.Add(new ConversationSummary
        {
            Conversation = conversation,
            UserId = 501,
            Content = "用户在追问上一轮问题。",
            ThroughMessageId = firstAnswer.Id,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync();

        var snapshot = await ConversationContextSnapshot.LoadAsync(
            _db, 501, conversation.Id, long.MaxValue, now, default);

        Assert.Equal("用户在追问上一轮问题。", snapshot.Summary);
        var recent = Assert.Single(snapshot.RecentMessages);
        Assert.Equal(followUp.Id, recent.Id);
        Assert.Contains("那我为什么会重复问", snapshot.CacheValue);
        Assert.DoesNotContain("我上一个问题是什么", snapshot.CacheValue);
    }

    private Event AddEvent(long userId, string title)
    {
        var now = DateTimeOffset.UtcNow;
        var evt = Event.Create(userId, EventKind.Trace, title, title, now, null, "UTC", Guid.NewGuid().ToString("N"), now);
        evt.SourceRevisions.Add(SourceRevision.Create(0, 1, title, title, now, null, now));
        _db.Events.Add(evt);
        return evt;
    }

    private static StorylineNodeInput Existing(Guid key, long eventId, int revision, Guid? stage, int order) =>
        new(key, "existing-event", eventId, revision, null, stage, order);
}
