namespace PassingTrace.Core.Events;

/// <summary>
/// Event 持久化端口。应用层只依赖此接口，不直接接触 DbContext。
/// </summary>
public interface IEventRepository
{
    /// <summary>按所有权查询 Event 及其 Source 修订历史。</summary>
    Task<Event?> FindAsync(long userId, long eventId, CancellationToken cancellationToken);

    /// <summary>按所有权与幂等键查询 Event，用于幂等创建去重。</summary>
    Task<Event?> FindByIdempotencyKeyAsync(
        long userId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>按所有权查询 Event 列表。</summary>
    Task<IReadOnlyList<Event>> ListAsync(
        EventListQuery query,
        CancellationToken cancellationToken);

    /// <summary>登记新 Event。</summary>
    void Add(Event evt);

    /// <summary>登记一条 Source 修订快照。</summary>
    void Add(SourceRevision revision);

    /// <summary>提交所有待处理变更。</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
