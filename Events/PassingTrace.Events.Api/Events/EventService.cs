using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Events;

/// <summary>
/// Event 应用编排：封装创建、查询、修改 Source 与软删除用例，
/// 不直接接触 DbContext。
/// </summary>
public sealed class EventService(IEventRepository repository, TimeProvider clock)
{
    /// <summary>创建 Event 并写入初始 Source 修订，幂等键冲突时复用或拒绝。</summary>
    public async Task<Event> CreateAsync(
        CreateEventCommand command,
        CancellationToken cancellationToken)
    {
        EnsureContent(command.Title, command.RawContent);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await repository.FindByIdempotencyKeyAsync(
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

        var now = clock.GetUtcNow();
        var evt = Event.Create(
            command.UserId,
            command.Kind,
            command.Title,
            command.RawContent,
            command.HappenedAt,
            command.PlannedAt,
            NormalizeTimezone(command.Timezone),
            command.IdempotencyKey,
            now);

        evt.SourceRevisions.Add(SourceRevision.Create(
            evt.Id,
            1,
            command.Title,
            command.RawContent,
            command.HappenedAt,
            command.PlannedAt,
            now));

        repository.Add(evt);
        await repository.SaveChangesAsync(cancellationToken);
        return evt;
    }

    /// <summary>按所有权查询 Event 详情。</summary>
    public Task<Event?> GetAsync(
        long userId,
        long eventId,
        CancellationToken cancellationToken)
    {
        return repository.FindAsync(userId, eventId, cancellationToken);
    }

    /// <summary>按条件查询 Event 列表。</summary>
    public Task<IReadOnlyList<Event>> ListAsync(
        EventListQuery query,
        CancellationToken cancellationToken)
    {
        return repository.ListAsync(query, cancellationToken);
    }

    /// <summary>修改 Source：递增修订版本并追加历史快照。</summary>
    public async Task<Event> UpdateSourceAsync(
        UpdateEventCommand command,
        CancellationToken cancellationToken)
    {
        EnsureContent(command.Title, command.RawContent);

        var evt = await repository.FindAsync(
                command.UserId,
                command.EventId,
                cancellationToken)
            ?? throw new EventNotFoundException(command.UserId, command.EventId);

        EnsureActive(evt, command.UserId);
        EnsureVersion(evt, command.ExpectedVersion);

        var now = clock.GetUtcNow();
        var nextRevision = evt.CurrentSourceRevision + 1;

        evt.Title = command.Title;
        evt.RawContent = command.RawContent;
        evt.HappenedAt = command.HappenedAt;
        evt.PlannedAt = command.PlannedAt;
        evt.Timezone = NormalizeTimezone(command.Timezone);
        evt.CurrentSourceRevision = nextRevision;
        evt.UpdatedAt = now;

        evt.SourceRevisions.Add(SourceRevision.Create(
            evt.Id,
            nextRevision,
            command.Title,
            command.RawContent,
            command.HappenedAt,
            command.PlannedAt,
            now));

        await repository.SaveChangesAsync(cancellationToken);
        return evt;
    }

    /// <summary>软删除 Event，进入可恢复删除期。</summary>
    public async Task SoftDeleteAsync(
        long userId,
        long eventId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        var evt = await repository.FindAsync(userId, eventId, cancellationToken)
            ?? throw new EventNotFoundException(userId, eventId);

        if (evt.DeletedAt is not null)
        {
            return;
        }

        EnsureVersion(evt, expectedVersion);

        var now = clock.GetUtcNow();
        evt.DeletedAt = now;
        evt.UpdatedAt = now;

        await repository.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureContent(string? title, string? rawContent)
    {
        if (string.IsNullOrWhiteSpace(title) &&
            string.IsNullOrWhiteSpace(rawContent))
        {
            throw new DomainValidationException("标题与原文不能同时为空。");
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
            evt.HappenedAt == command.HappenedAt &&
            evt.PlannedAt == command.PlannedAt;
    }

    private static string NormalizeTimezone(string timezone)
    {
        return string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone;
    }
}
