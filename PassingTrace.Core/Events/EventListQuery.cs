namespace PassingTrace.Core.Events;

/// <summary>
/// Event 列表查询规格。From / To 过滤记录的发生或计划时间，未填写业务时间时
/// 回退到创建时间；游标对外使用 Event Id，仓储按业务时间 + Id 复合倒序分页。
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
    IReadOnlyList<string>? TagKeys = null,
    string? Query = null);
