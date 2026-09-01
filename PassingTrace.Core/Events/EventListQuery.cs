namespace PassingTrace.Core.Events;

/// <summary>
/// Event 列表查询规格。From / To 过滤记录的发生或计划时间，未填写业务时间时
/// 回退到创建时间；游标分页使用 Id 作为稳定游标，按创建时间倒序返回。
/// </summary>
public sealed record EventListQuery(
    long UserId,
    EventKind? Kind = null,
    EventStatus? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool IncludeDeleted = false,
    int Limit = 50,
    long? Cursor = null,
    string? CategoryKey = null,
    IReadOnlyList<string>? TagKeys = null);
