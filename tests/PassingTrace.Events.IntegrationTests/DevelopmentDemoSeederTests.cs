using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Core.Storylines;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Development;
using PassingTrace.Events.Api.Events;
using PassingTrace.Events.Api.Media;
using PassingTrace.Events.Api.Storylines;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class DevelopmentDemoSeederTests(StorylinePostgresFixture fixture)
    : IClassFixture<StorylinePostgresFixture>
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 14, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initial_seed_uses_domain_services_and_creates_complete_graphs()
    {
        const long userId = 8101;
        await using var db = new TraceDbContext(fixture.Options);

        var result = await Seeder(db).SeedAsync(userId, default);

        Assert.Equal(new DevelopmentDemoResult(30, 0, 5, 0, 0), result);
        var events = await db.Events.AsNoTracking().Where(x => x.UserId == userId)
            .Include(x => x.SourceRevisions).ThenInclude(x => x.Labels)
            .Include(x => x.SearchIndexes).ToListAsync();
        Assert.Equal(DevelopmentDemoCatalog.Events(Anchor).Select(x => DevelopmentDemoSeeder.Key("event", x.Key)).Order(),
            events.Select(x => x.IdempotencyKey).Order());
        Assert.All(events, item =>
        {
            Assert.Equal(1, item.CurrentSourceRevision);
            Assert.Equal("Asia/Shanghai", item.Timezone);
            var revision = Assert.Single(item.SourceRevisions);
            Assert.Contains(revision.Labels, x => x.DisplayName == "开发示例");
            Assert.Contains(revision.Labels, x => x.Type == EventLabelType.PrimaryCategory);
            Assert.Single(item.SearchIndexes, x => x.IsCurrent);
        });
        Assert.Contains(events, x => x.EventKind == EventKind.Plan && x.Status == EventStatus.Completed && x.CompletedAt == x.PlannedAt);
        Assert.Contains(events, x => x.Status == EventStatus.Cancelled);
        Assert.Contains(events, x => x.EventKind == EventKind.Plan && x.Status == EventStatus.Planned && x.PlannedAt > Anchor);
        Assert.True(await db.EventLabelIndexes.AnyAsync(x => x.UserId == userId && x.IsCurrent));
        Assert.True(await db.EventLocations.AnyAsync(x => x.UserId == userId));

        var storylines = await db.Storylines.AsNoTracking().Where(x => x.UserId == userId)
            .Include(x => x.Revisions).ThenInclude(x => x.Nodes).ThenInclude(x => x.Event)
            .Include(x => x.Revisions).ThenInclude(x => x.Edges)
            .Include(x => x.Revisions).ThenInclude(x => x.WebLayout).ThenInclude(x => x!.Nodes)
            .Include(x => x.SearchIndexes).AsSplitQuery().ToListAsync();
        Assert.Equal(2, storylines.Count(x => x.Status == StorylineStatus.Completed));
        Assert.Equal(3, storylines.Count(x => x.Status == StorylineStatus.Ongoing));
        Assert.Equal(5, storylines.Select(x => x.CreationIdempotencyKey).Distinct().Count());
        Assert.All(storylines, storyline =>
        {
            Assert.StartsWith("development-demo-v1-storyline-", storyline.CreationIdempotencyKey);
            var revision = Assert.Single(storyline.Revisions);
            Assert.NotEmpty(revision.Nodes);
            Assert.Equal(revision.Nodes.Count, revision.WebLayout!.Nodes.Count);
            Assert.All(revision.Nodes, node =>
            {
                Assert.Equal(userId, node.Event.UserId);
                Assert.Equal(node.Event.CurrentSourceRevision, node.SourceRevision);
            });
            Assert.Single(storyline.SearchIndexes, x => x.IsCurrent);
            if (storyline.Status == StorylineStatus.Completed)
            {
                Assert.Contains(revision.Edges, edge => edge.RelationType == StorylineRelationType.Branch);
                Assert.Contains(revision.Edges.GroupBy(x => x.TargetNodeKey), group => group.Count() > 1);
            }
        });
        Assert.Equal(30, await db.OutboxMessages.CountAsync(x => x.UserId == userId && x.MessageType == "event.analyze"));
        Assert.Equal(5, await db.OutboxMessages.CountAsync(x => x.UserId == userId && x.MessageType == "storyline.index"));
        Assert.Equal(35, (await db.UserDataWatermarks.SingleAsync(x => x.UserId == userId)).Version);
    }

    [Fact]
    public async Task Replay_next_month_preserves_edits_soft_deletes_and_manual_data_without_new_work()
    {
        const long userId = 8102;
        await using var db = new TraceDbContext(fixture.Options);
        var manual = await EventService(db).CreateAsync(new CreateEventCommand(userId, EventKind.Trace,
            "我自己的记录", "保留这条", Anchor, null, "UTC", "manual-record"), default);
        await Seeder(db).SeedAsync(userId, default);
        db.ChangeTracker.Clear();
        var edited = await db.Events.SingleAsync(x => x.UserId == userId && x.IdempotencyKey == DevelopmentDemoSeeder.Key("event", "project-notes"));
        await EventService(db).UpdateSourceAsync(new UpdateEventCommand(userId, edited.Id, edited.RowVersion,
            "用户修改过的标题", "用户修改过的正文", edited.HappenedAt, null, edited.Timezone), default);
        var deleted = await db.Events.SingleAsync(x => x.UserId == userId && x.IdempotencyKey == DevelopmentDemoSeeder.Key("event", "daily-shopping"));
        await EventService(db).SoftDeleteAsync(userId, deleted.Id, deleted.RowVersion, default);
        var records = await RecordsByKey(db, userId);
        var editedStory = await db.Storylines.SingleAsync(x => x.UserId == userId && x.CreationIdempotencyKey == DevelopmentDemoSeeder.Key("storyline", "personal-site"));
        var request = DevelopmentDemoCatalog.Storylines(records).Single(x => x.Key == "personal-site").Request;
        await StorylineService(db).SaveAsync(userId, editedStory.Id, editedStory.RowVersion,
            request with { Title = "用户改过的故事线" }, "user-story-edit", default);
        var deletedStory = await db.Storylines.SingleAsync(x => x.UserId == userId && x.CreationIdempotencyKey == DevelopmentDemoSeeder.Key("storyline", "west-lake-weekend"));
        await StorylineService(db).DeleteAsync(userId, deletedStory.Id, deletedStory.RowVersion, default);
        db.ChangeTracker.Clear();
        var dates = await db.Events.Where(x => x.UserId == userId).ToDictionaryAsync(x => x.Id, x => new { x.HappenedAt, x.PlannedAt });
        var watermark = (await db.UserDataWatermarks.AsNoTracking().SingleAsync(x => x.UserId == userId)).Version;
        var outboxCount = await db.OutboxMessages.CountAsync(x => x.UserId == userId);

        var replay = await Seeder(db, Anchor.AddMonths(1)).SeedAsync(userId, default);

        Assert.Equal(new DevelopmentDemoResult(0, 30, 0, 5, 0), replay);
        db.ChangeTracker.Clear();
        var all = await db.Events.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
        Assert.Equal(31, all.Count);
        Assert.All(all, item =>
        {
            Assert.Equal(dates[item.Id].HappenedAt, item.HappenedAt);
            Assert.Equal(dates[item.Id].PlannedAt, item.PlannedAt);
        });
        Assert.Equal("我自己的记录", all.Single(x => x.Id == manual.Id).Title);
        Assert.Equal("用户修改过的标题", all.Single(x => x.Id == edited.Id).Title);
        Assert.Equal(2, all.Single(x => x.Id == edited.Id).CurrentSourceRevision);
        Assert.NotNull(all.Single(x => x.Id == deleted.Id).DeletedAt);
        var persistedStories = await db.Storylines.Where(x => x.UserId == userId).ToListAsync();
        Assert.Equal(5, persistedStories.Count);
        Assert.Equal("用户改过的故事线", persistedStories.Single(x => x.Id == editedStory.Id).Title);
        Assert.Equal(2, persistedStories.Single(x => x.Id == editedStory.Id).CurrentRevision);
        Assert.NotNull(persistedStories.Single(x => x.Id == deletedStory.Id).DeletedAt);
        Assert.Equal(watermark, (await db.UserDataWatermarks.SingleAsync(x => x.UserId == userId)).Version);
        Assert.Equal(outboxCount, await db.OutboxMessages.CountAsync(x => x.UserId == userId));
    }

    [Fact]
    public async Task Partial_seed_fills_missing_records_uses_current_revisions_and_skips_deleted_dependencies()
    {
        const long userId = 8103;
        await using var db = new TraceDbContext(fixture.Options);
        var catalog = DevelopmentDemoCatalog.Events(Anchor);
        var deleted = await CreateFixture(db, userId, catalog.Single(x => x.Key == "trip-ticket"));
        await EventService(db).SoftDeleteAsync(userId, deleted.Id, deleted.RowVersion, default);
        var edited = await CreateFixture(db, userId, catalog.Single(x => x.Key == "project-notes"));
        await EventService(db).UpdateSourceAsync(new UpdateEventCommand(userId, edited.Id, edited.RowVersion,
            "已有的项目笔记", "保存最新修订", edited.HappenedAt, null, edited.Timezone), default);

        var result = await Seeder(db).SeedAsync(userId, default);

        Assert.Equal(new DevelopmentDemoResult(28, 2, 4, 0, 1), result);
        Assert.Equal(30, await db.Events.CountAsync(x => x.UserId == userId));
        Assert.NotNull((await db.Events.AsNoTracking().SingleAsync(x => x.Id == deleted.Id)).DeletedAt);
        Assert.False(await db.Storylines.AnyAsync(x => x.UserId == userId && x.CreationIdempotencyKey == DevelopmentDemoSeeder.Key("storyline", "west-lake-weekend")));
        var node = await db.StorylineNodes.SingleAsync(x => x.EventId == edited.Id);
        Assert.Equal(2, node.SourceRevision);
        Assert.Equal(new DevelopmentDemoResult(0, 30, 0, 4, 1), await Seeder(db).SeedAsync(userId, default));
    }

    [Fact]
    public async Task Same_keys_for_another_user_create_independent_records_and_owned_graph_nodes()
    {
        await using var db = new TraceDbContext(fixture.Options);
        await Seeder(db).SeedAsync(8104, default);
        var second = await Seeder(db).SeedAsync(8105, default);

        Assert.Equal(new DevelopmentDemoResult(30, 0, 5, 0, 0), second);
        var firstIds = await db.Events.Where(x => x.UserId == 8104).Select(x => x.Id).ToListAsync();
        var secondNodes = await db.StorylineNodes.Where(x => x.Revision.Storyline.UserId == 8105)
            .Include(x => x.Event).ToListAsync();
        Assert.NotEmpty(secondNodes);
        Assert.All(secondNodes, node =>
        {
            Assert.Equal(8105, node.Event.UserId);
            Assert.DoesNotContain(node.EventId, firstIds);
        });
        Assert.Equal(30, await db.Events.CountAsync(x => x.UserId == 8104));
        Assert.Equal(5, await db.Storylines.CountAsync(x => x.UserId == 8104));
    }

    [Theory]
    [InlineData("Production", true, 8191)]
    [InlineData("Development", false, 8192)]
    [InlineData("Development", true, 0)]
    public async Task Guard_rejects_before_any_write(string environment, bool enabled, long userId)
    {
        await using var db = new TraceDbContext(fixture.Options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Seeder(db, environment: environment, enabled: enabled).SeedAsync(userId, default));

        Assert.Empty(db.ChangeTracker.Entries());
        Assert.False(await db.Events.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.Storylines.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.OutboxMessages.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.UserDataWatermarks.AnyAsync(x => x.UserId == userId));
    }

    private static async Task<Dictionary<string, Event>> RecordsByKey(TraceDbContext db, long userId)
    {
        var rows = await db.Events.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();
        return DevelopmentDemoCatalog.Events(Anchor).ToDictionary(x => x.Key,
            fixture => rows.Single(x => x.IdempotencyKey == DevelopmentDemoSeeder.Key("event", fixture.Key)));
    }

    private static Task<Event> CreateFixture(TraceDbContext db, long userId, DevelopmentDemoCatalog.DemoEvent fixture)
    {
        var input = fixture.Request;
        return EventService(db).CreateAsync(new CreateEventCommand(userId, input.Kind, input.Title, input.RawContent,
            input.HappenedAt, input.PlannedAt, input.Timezone!, DevelopmentDemoSeeder.Key("event", fixture.Key),
            input.MediaIds, input.Classification, input.Locations), default);
    }

    private static DevelopmentDemoSeeder Seeder(TraceDbContext db, DateTimeOffset? now = null,
        string environment = "Development", bool enabled = true) =>
        new(db, EventService(db, now), StorylineService(db, now), new FixedTimeProvider(now ?? Anchor),
            new TestEnvironment { EnvironmentName = environment }, Options.Create(new DevelopmentDemoOptions { Enabled = enabled }));

    private static EventService EventService(TraceDbContext db, DateTimeOffset? now = null) =>
        new(new EventRepository(db), new FixedTimeProvider(now ?? Anchor), new NoMedia(), new AnalysisOutbox(db));

    private static StorylineService StorylineService(TraceDbContext db, DateTimeOffset? now = null) =>
        new(db, new AnalysisOutbox(db), new FixedTimeProvider(now ?? Anchor));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoMedia : IEventMediaService
    {
        public Task<IReadOnlyList<MediaAsset>> ResolveAsync(long userId, IReadOnlyList<Guid>? mediaIds, CancellationToken cancellationToken)
        {
            Assert.True(mediaIds is null or { Count: 0 });
            return Task.FromResult<IReadOnlyList<MediaAsset>>([]);
        }

        public void ReplaceCurrent(Event evt, SourceRevision revision, IReadOnlyList<MediaAsset> media, DateTimeOffset now) => Assert.Empty(media);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "DevelopmentDemoSeederTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
