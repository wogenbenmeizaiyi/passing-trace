namespace PassingTrace.Core.Events;

/// <summary>
/// Event 列表查询规格。游标分页使用 Id 作为稳定游标，
/// 按创建时间倒序返回。
/// </summary>
public sealed record EventListQuery(
    long UserId,
    EventKind? Kind = null,
    EventStatus? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool IncludeDeleted = false,
    int Limit = 50,
    long? Cursor = null);
