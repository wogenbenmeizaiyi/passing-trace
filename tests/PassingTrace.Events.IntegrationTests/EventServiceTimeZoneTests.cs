using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PassingTrace.Events.Api.Events;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence;
using PassingTrace.Core.Events;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class EventServiceTimeZoneTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly TraceDbContext _db;

    public EventServiceTimeZoneTests()
    {
        _connection.Open();
        _db = CreateDbContext(_connection);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateAsync_NormalizesNonUtcOffsets_BeforePersisting()
    {
        var service = CreateService(_db);
        var happened = new DateTimeOffset(2026, 8, 18, 19, 30, 0, TimeSpan.FromHours(8));

        var evt = await service.CreateAsync(
            new CreateEventCommand(
                UserId: 1,
                Kind: EventKind.Trace,
                Title: "和朋友吃了烤肉",
                RawContent: "今天和朋友去了涩谷吃烤肉。",
                HappenedAt: happened,
                PlannedAt: null,
                Timezone: "Asia/Tokyo",
                IdempotencyKey: "tz-test-1"),
            CancellationToken.None);

        var reloaded = await _db.Events
            .Include(e => e.SourceRevisions)
            .AsNoTracking()
            .SingleAsync(e => e.Id == evt.Id, CancellationToken.None);

        // +08:00 的时刻应被归一化为同一 UTC 时刻（Offset = 0）。
        Assert.Equal(happened.ToUniversalTime(), reloaded.HappenedAt);
        Assert.Equal(TimeSpan.Zero, reloaded.HappenedAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, reloaded.SourceRevisions[0].HappenedAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, reloaded.CreatedAt.Offset);
    }

    [Fact]
    public async Task UpdateSourceAsync_NormalizesNonUtcOffsets_BeforePersisting()
    {
        var service = CreateService(_db);
        var created = await service.CreateAsync(
            new CreateEventCommand(
                UserId: 2,
                Kind: EventKind.Plan,
                Title: "周末露营",
                RawContent: "计划周六去湖边露营。",
                HappenedAt: null,
                PlannedAt: null,
                Timezone: "UTC",
                IdempotencyKey: "tz-test-2"),
            CancellationToken.None);

        var planned = new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.FromHours(-5));
        await service.UpdateSourceAsync(
            new UpdateEventCommand(
                UserId: 2,
                EventId: created.Id,
                ExpectedVersion: created.RowVersion,
                Title: "周末露营",
                RawContent: "计划周六去湖边露营。",
                HappenedAt: null,
                PlannedAt: planned,
                Timezone: "America/New_York"),
            CancellationToken.None);

        var reloaded = await _db.Events
            .AsNoTracking()
            .SingleAsync(e => e.Id == created.Id, CancellationToken.None);

        Assert.Equal(planned.ToUniversalTime(), reloaded.PlannedAt);
        Assert.Equal(TimeSpan.Zero, reloaded.PlannedAt!.Value.Offset);
        Assert.Equal(2, reloaded.CurrentSourceRevision);
    }

    private static TraceDbContext CreateDbContext() =>
        throw new InvalidOperationException("请使用带连接的构造器。");

    private static TraceDbContext CreateDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<TraceDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteModelCustomizer>()
            .Options;
        return new TraceDbContext(options);
    }

    private static EventService CreateService(TraceDbContext db) =>
        new(new EventRepository(db), TimeProvider.System);

    /// <summary>
    /// SQLite 没有 PostgreSQL 的 xmin 系统列。把 RowVersion 的 IsRowVersion()
    /// 约定替换为普通列，避免插入时 NOT NULL 失败。
    /// </summary>
    private sealed class SqliteModelCustomizer : ModelCustomizer
    {
        public SqliteModelCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies)
        {
        }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Event>()
                .Property(e => e.RowVersion)
                .ValueGeneratedNever();
        }
    }
}
