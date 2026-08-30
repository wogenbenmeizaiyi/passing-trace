using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PassingTrace.Events.Api.Events;
using PassingTrace.Infrastructure;
using PassingTrace.Infrastructure.Persistence;
using PassingTrace.Core.Events;
using PassingTrace.Core.Ai;
using PassingTrace.Core.Media;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Media;
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

    [Fact]
    public async Task CreateAsync_AllowsMediaOnly_AndSnapshotsAttachment()
    {
        var asset = AddReadyMedia(userId: 31, "photo.jpg", MediaKind.Image);
        await _db.SaveChangesAsync();
        var service = CreateMediaAwareService(_db);

        var created = await service.CreateAsync(new CreateEventCommand(
            UserId: 31, Kind: EventKind.Trace, Title: null, RawContent: null,
            HappenedAt: null, PlannedAt: null, Timezone: "UTC", IdempotencyKey: "media-only",
            MediaIds: [asset.Id]), CancellationToken.None);

        var reloaded = await _db.Events.AsNoTracking()
            .Include(x => x.MediaAssets)
            .Include(x => x.SourceRevisions).ThenInclude(x => x.MediaAssets)
            .SingleAsync(x => x.Id == created.Id);
        Assert.Single(reloaded.MediaAssets);
        Assert.Equal(asset.Id, reloaded.MediaAssets[0].MediaAssetId);
        Assert.Single(reloaded.SourceRevisions[0].MediaAssets);
        Assert.Equal(asset.Id, reloaded.SourceRevisions[0].MediaAssets[0].MediaAssetId);
    }

    [Fact]
    public async Task UpdateSourceAsync_PreservesOldMediaSnapshot()
    {
        var first = AddReadyMedia(32, "first.pdf", MediaKind.File);
        var second = AddReadyMedia(32, "second.pdf", MediaKind.File);
        await _db.SaveChangesAsync();
        var service = CreateMediaAwareService(_db);
        var created = await service.CreateAsync(new CreateEventCommand(
            32, EventKind.Trace, null, null, null, null, "UTC", "revision-media", [first.Id]),
            CancellationToken.None);

        await service.UpdateSourceAsync(new UpdateEventCommand(
            32, created.Id, created.RowVersion, null, null, null, null, "UTC", [second.Id]),
            CancellationToken.None);

        var revisions = await _db.SourceRevisions.AsNoTracking()
            .Include(x => x.MediaAssets)
            .Where(x => x.EventId == created.Id)
            .OrderBy(x => x.Revision)
            .ToListAsync();
        Assert.Equal(2, revisions.Count);
        Assert.Equal(first.Id, Assert.Single(revisions[0].MediaAssets).MediaAssetId);
        Assert.Equal(second.Id, Assert.Single(revisions[1].MediaAssets).MediaAssetId);
    }

    [Fact]
    public async Task ResolveAsync_RejectsAttachmentOwnedByAnotherUser()
    {
        var foreign = AddReadyMedia(40, "private.png", MediaKind.Image);
        await _db.SaveChangesAsync();
        var media = CreateMediaService(_db);

        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            media.ResolveAsync(41, [foreign.Id], CancellationToken.None));

        Assert.Contains("不属于当前用户", error.Message);
    }

    [Fact]
    public async Task ConfirmAsync_RejectsImageWhoseDeclaredMimeDoesNotMatchMagicBytes()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var now = DateTimeOffset.UtcNow;
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            UserId = 45,
            ObjectKey = "tests/spoofed.jpg",
            OriginalFileName = "spoofed.jpg",
            Kind = MediaKind.Image,
            DeclaredMimeType = "image/jpeg",
            ExpectedSize = bytes.Length,
            ExpectedSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Status = MediaAssetStatus.PendingUpload,
            UploadMode = MediaUploadMode.Single,
            UploadExpiresAt = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.MediaAssets.Add(asset);
        await _db.SaveChangesAsync();
        var storage = new MemoryObjectStorage(bytes);
        var media = new MediaService(_db, storage, new AnalysisOutbox(_db), TimeProvider.System);

        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            media.ConfirmAsync(45, asset.Id, new ConfirmMediaUploadRequest(null), CancellationToken.None));

        Assert.Contains("声明类型", error.Message);
        Assert.True(storage.Deleted);
        Assert.Equal(MediaAssetStatus.Failed, asset.Status);
    }

    [Fact]
    public async Task UserMemoryService_UpdatesCurrentUser_AndCannotRejectForeignMemory()
    {
        var now = DateTimeOffset.UtcNow;
        var mine = new UserMemory
        {
            UserId = 51,
            Type = UserMemoryType.Preference,
            Content = "喜欢徒步",
            Confidence = 0.9m,
            Status = UserMemoryStatus.Automatic,
            Fingerprint = new string('a', 64),
            CreatedAt = now,
            UpdatedAt = now,
        };
        var foreign = new UserMemory
        {
            UserId = 52,
            Type = UserMemoryType.Profile,
            Content = "住在别处",
            Confidence = 0.8m,
            Status = UserMemoryStatus.Confirmed,
            Fingerprint = new string('b', 64),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.UserMemories.AddRange(mine, foreign);
        await _db.SaveChangesAsync();
        var service = new UserMemoryService(_db, CreateCurrentUser(51), null!, TimeProvider.System);

        var updated = await service.UpdateAsync(
            mine.Id,
            new UpdateUserMemoryRequest(null, null, "Confirmed"),
            CancellationToken.None);

        Assert.Equal(mine.Id, updated.Id);
        Assert.Equal("Confirmed", updated.Status);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RejectAsync(foreign.Id, CancellationToken.None));
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

    private static EventService CreateMediaAwareService(TraceDbContext db) =>
        new(new EventRepository(db), TimeProvider.System, CreateMediaService(db), new AnalysisOutbox(db));

    private static MediaService CreateMediaService(TraceDbContext db) =>
        new(db, new UnusedObjectStorage(), new AnalysisOutbox(db), TimeProvider.System);

    private static CurrentUserContext CreateCurrentUser(long userId)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")),
        };
        return new CurrentUserContext(new HttpContextAccessor { HttpContext = context });
    }

    private MediaAsset AddReadyMedia(long userId, string fileName, MediaKind kind)
    {
        var now = DateTimeOffset.UtcNow;
        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ObjectKey = $"tests/{Guid.NewGuid():N}",
            OriginalFileName = fileName,
            Kind = kind,
            DeclaredMimeType = kind == MediaKind.Image ? "image/png" : "application/pdf",
            VerifiedMimeType = kind == MediaKind.Image ? "image/png" : "application/pdf",
            ExpectedSize = 4,
            ActualSize = 4,
            ExpectedSha256 = new string('0', 64),
            ActualSha256 = new string('0', 64),
            Status = MediaAssetStatus.Ready,
            UploadMode = MediaUploadMode.Single,
            UploadExpiresAt = now.AddHours(1),
            ConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.MediaAssets.Add(asset);
        return asset;
    }

    private sealed class UnusedObjectStorage : IObjectStorage
    {
        private static Exception Unused() => new InvalidOperationException("对象存储不应在此测试中被调用。");
        public Task EnsureBucketAsync(CancellationToken cancellationToken) => throw Unused();
        public Task<string> CreateMultipartUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken) => throw Unused();
        public Task<Uri> CreateUploadUrlAsync(string objectKey, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw Unused();
        public Task<Uri> CreatePartUploadUrlAsync(string objectKey, string uploadId, int partNumber, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw Unused();
        public Task CompleteMultipartUploadAsync(string objectKey, string uploadId, IReadOnlyList<CompletedPart> parts, CancellationToken cancellationToken) => throw Unused();
        public Task AbortMultipartUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken) => throw Unused();
        public Task<StoredObjectInfo> GetInfoAsync(string objectKey, CancellationToken cancellationToken) => throw Unused();
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) => throw Unused();
        public Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken) => throw Unused();
        public Task<Uri> CreateDownloadUrlAsync(string objectKey, string fileName, string contentType, bool inline, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw Unused();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => throw Unused();
    }

    private sealed class MemoryObjectStorage(byte[] content) : IObjectStorage
    {
        private static Exception Unused() => new InvalidOperationException("对象存储方法不应在此测试中被调用。");
        public bool Deleted { get; private set; }
        public Task EnsureBucketAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> CreateMultipartUploadAsync(string objectKey, string contentType, CancellationToken cancellationToken) => throw Unused();
        public Task<Uri> CreateUploadUrlAsync(string objectKey, string contentType, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw Unused();
        public Task<Uri> CreatePartUploadUrlAsync(string objectKey, string uploadId, int partNumber, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw Unused();
        public Task CompleteMultipartUploadAsync(string objectKey, string uploadId, IReadOnlyList<CompletedPart> parts, CancellationToken cancellationToken) => throw Unused();
        public Task AbortMultipartUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken) => throw Unused();
        public Task<StoredObjectInfo> GetInfoAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult(new StoredObjectInfo(content.Length, "image/png"));
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(content, writable: false));
        public Task PutAsync(string objectKey, Stream stream, string contentType, CancellationToken cancellationToken) => throw Unused();
        public Task<Uri> CreateDownloadUrlAsync(string objectKey, string fileName, string contentType, bool inline, DateTimeOffset expiresAt, CancellationToken cancellationToken) => throw Unused();
        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            Deleted = true;
            return Task.CompletedTask;
        }
    }

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
            modelBuilder.Entity<EventSearchIndex>().Ignore("Embedding");
            modelBuilder.Entity<EventSearchIndex>().Ignore("SearchVector");
            modelBuilder.Entity<UserMemory>().Ignore("Embedding");
        }
    }
}
