using PassingTrace.Core.Events;
using PassingTrace.Core.Media;
using PassingTrace.Events.Api.Ai;
using PassingTrace.Events.Api.Media;

namespace PassingTrace.Events.Api.Events;

/// <summary>
/// Event 应用编排：封装创建、查询、修改 Source 与软删除用例，
/// 不直接接触 DbContext。
/// </summary>
/// 
public sealed class EventService
{
    private readonly IEventRepository _repository;
    private readonly TimeProvider _clock;
    private readonly IEventMediaService _mediaService;
    private readonly IAnalysisOutbox _outbox;

    public EventService(IEventRepository repository, TimeProvider clock)
        : this(repository, clock, new NoopEventMediaService(), new NoopAnalysisOutbox())
    {
    }

    public EventService(
        IEventRepository repository,
        TimeProvider clock,
        IEventMediaService mediaService,
        IAnalysisOutbox outbox)
    {
        _repository = repository;
        _clock = clock;
        _mediaService = mediaService;
        _outbox = outbox;
    }


    /// <summary>创建 Event 并写入初始 Source 修订，幂等键冲突时复用或拒绝。</summary>
    public async Task<Event> CreateAsync(
        CreateEventCommand command,
        CancellationToken cancellationToken)
    {
        var media = await _mediaService.ResolveAsync(command.UserId, command.MediaIds, cancellationToken);
        EnsureContent(command.Title, command.RawContent, media.Count);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await _repository.FindByIdempotencyKeyAsync(
                command.UserId,
                command.IdempotencyKey,
                cancellationToken);

            if (existing is not null)
            {
                if (!MatchesContent(existing, command))
                {
                    throw new IdempotencyConflictException(command.IdempotencyKey);
                }

                return existing;
            }
        }

        var now = _clock.GetUtcNow();
        var evt = Event.Create(
            command.UserId,
            command.Kind,
            command.Title,
            command.RawContent,
            ToUtc(command.HappenedAt),
            ToUtc(command.PlannedAt),
            NormalizeTimezone(command.Timezone),
            command.IdempotencyKey,
            now);

        var revision = SourceRevision.Create(
            evt.Id,
            1,
            command.Title,
            command.RawContent,
            ToUtc(command.HappenedAt),
            ToUtc(command.PlannedAt),
            now);
        evt.SourceRevisions.Add(revision);
        _mediaService.ReplaceCurrent(evt, revision, media, now);
        _outbox.EnqueueEvent(evt, 1, now);
        await _outbox.IncrementWatermarkAsync(command.UserId, now, cancellationToken);

        _repository.Add(evt);
        await _repository.SaveChangesAsync(cancellationToken);
        return evt;
    }

    /// <summary>按所有权查询 Event 详情。</summary>
    public Task<Event?> GetAsync(
        long userId,
        long eventId,
        CancellationToken cancellationToken)
    {
        return _repository.FindAsync(userId, eventId, cancellationToken);
    }

    /// <summary>按条件查询 Event 列表。</summary>
    public Task<IReadOnlyList<Event>> ListAsync(
        EventListQuery query,
        CancellationToken cancellationToken)
    {
        return _repository.ListAsync(query, cancellationToken);
    }

    /// <summary>修改 Source：递增修订版本并追加历史快照。</summary>
    public async Task<Event> UpdateSourceAsync(
        UpdateEventCommand command,
        CancellationToken cancellationToken)
    {
        var media = await _mediaService.ResolveAsync(command.UserId, command.MediaIds, cancellationToken);
        EnsureContent(command.Title, command.RawContent, media.Count);

        var evt = await _repository.FindAsync(
                command.UserId,
                command.EventId,
                cancellationToken)
            ?? throw new EventNotFoundException(command.UserId, command.EventId);

        EnsureActive(evt, command.UserId);
        EnsureVersion(evt, command.ExpectedVersion);

        var now = _clock.GetUtcNow();
        var nextRevision = evt.CurrentSourceRevision + 1;

        evt.Title = command.Title;
        evt.RawContent = command.RawContent;
        evt.HappenedAt = ToUtc(command.HappenedAt);
        evt.PlannedAt = ToUtc(command.PlannedAt);
        evt.Timezone = NormalizeTimezone(command.Timezone);
        evt.CurrentSourceRevision = nextRevision;
        evt.UpdatedAt = now;

        var revision = SourceRevision.Create(
            evt.Id,
            nextRevision,
            command.Title,
            command.RawContent,
            ToUtc(command.HappenedAt),
            ToUtc(command.PlannedAt),
            now);
        evt.SourceRevisions.Add(revision);
        _mediaService.ReplaceCurrent(evt, revision, media, now);
        _outbox.EnqueueEvent(evt, nextRevision, now);
        await _outbox.IncrementWatermarkAsync(command.UserId, now, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);
        return evt;
    }

    /// <summary>软删除 Event，进入可恢复删除期。</summary>
    public async Task SoftDeleteAsync(
        long userId,
        long eventId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        var evt = await _repository.FindAsync(userId, eventId, cancellationToken)
            ?? throw new EventNotFoundException(userId, eventId);

        if (evt.DeletedAt is not null)
        {
            return;
        }

        EnsureVersion(evt, expectedVersion);

        var now = _clock.GetUtcNow();
        evt.DeletedAt = now;
        evt.UpdatedAt = now;

        _outbox.EnqueueEvent(evt, evt.CurrentSourceRevision, now, messageType: "event.deleted");
        await _outbox.IncrementWatermarkAsync(userId, now, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureContent(string? title, string? rawContent, int mediaCount)
    {
        if (string.IsNullOrWhiteSpace(title) &&
            string.IsNullOrWhiteSpace(rawContent) &&
            mediaCount == 0)
        {
            throw new DomainValidationException("标题、原文和附件不能同时为空。");
        }
    }

    private static void EnsureActive(Event evt, long userId)
    {
        if (evt.DeletedAt is not null)
        {
            throw new EventNotFoundException(userId, evt.Id);
        }
    }

    private static void EnsureVersion(Event evt, uint expectedVersion)
    {
        if (evt.RowVersion != expectedVersion)
        {
            throw new ConcurrencyException("事件版本已过期，请刷新后重试。");
        }
    }

    private static bool MatchesContent(Event evt, CreateEventCommand command)
    {
        return evt.EventKind == command.Kind &&
            evt.Title == command.Title &&
            evt.RawContent == command.RawContent &&
            evt.HappenedAt == ToUtc(command.HappenedAt) &&
            evt.PlannedAt == ToUtc(command.PlannedAt) &&
            evt.MediaAssets.OrderBy(x => x.SortOrder).Select(x => x.MediaAssetId)
                .SequenceEqual(command.MediaIds ?? []);
    }

    /// <summary>归一化为 UTC，满足 Npgsql timestamp with time zone 只接受 Offset=0 的要求。</summary>
    private static DateTimeOffset? ToUtc(DateTimeOffset? value) => value?.ToUniversalTime();

    private static string NormalizeTimezone(string timezone)
    {
        return string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone;
    }

    private sealed class NoopEventMediaService : IEventMediaService
    {
        public Task<IReadOnlyList<MediaAsset>> ResolveAsync(long userId, IReadOnlyList<Guid>? mediaIds, CancellationToken cancellationToken)
        {
            if (mediaIds is { Count: > 0 })
            {
                throw new InvalidOperationException("未配置附件服务。");
            }
            return Task.FromResult<IReadOnlyList<MediaAsset>>([]);
        }

        public void ReplaceCurrent(Event evt, SourceRevision revision, IReadOnlyList<MediaAsset> media, DateTimeOffset now)
        {
        }
    }

    private sealed class NoopAnalysisOutbox : IAnalysisOutbox
    {
        public void EnqueueEvent(Event evt, int sourceRevision, DateTimeOffset now, int priority = 100, string messageType = "event.analyze")
        {
        }

        public void EnqueueMedia(long userId, Guid mediaAssetId, DateTimeOffset now, int priority = 100)
        {
        }

        public Task IncrementWatermarkAsync(long userId, DateTimeOffset now, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
