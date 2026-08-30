namespace PassingTrace.Core.Events;

using PassingTrace.Core.Media;
using PassingTrace.Core.Ai;

/// <summary>
/// Event 聚合根，Trace 与 Plan 统一抽象。Event 表保存当前 Source 值，
/// 每次修改写入一条 <see cref="SourceRevision"/> 历史快照并递增版本。
/// </summary>
public sealed class Event
{
    /// <summary>Event 主键。</summary>
    public long Id { get; set; }

    /// <summary>归属用户，引用 Identity 的 user_id，业务层以它做所有权隔离。</summary>
    public long UserId { get; set; }

    /// <summary>创建来源，创建后不可变更。</summary>
    public EventKind EventKind { get; set; }

    /// <summary>生命周期状态。</summary>
    public EventStatus Status { get; set; }

    /// <summary>当前 Source 的标题，可为空；标题、原文、附件至少存在一种。</summary>
    public string? Title { get; set; }

    /// <summary>当前 Source 的用户原始自然语言，AI 不得覆盖。</summary>
    public string? RawContent { get; set; }

    /// <summary>当前 Source 的实际发生时间。</summary>
    public DateTimeOffset? HappenedAt { get; set; }

    /// <summary>当前 Source 的计划发生时间。</summary>
    public DateTimeOffset? PlannedAt { get; set; }

    /// <summary>执行“完成计划”操作的系统时间，与实际发生时间不同。</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>用户创建或确认事件时使用的时区，用于确定自然日边界。</summary>
    public string Timezone { get; set; } = "UTC";

    /// <summary>可见性，V1 恒为 private。</summary>
    public Visibility Visibility { get; set; } = Visibility.Private;

    /// <summary>幂等键，用于防止重复提交创建重复 Event。</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>当前 Source 修订版本，每次修改 Source 递增。</summary>
    public int CurrentSourceRevision { get; set; }

    /// <summary>归档时间，只影响默认列表展示，不改变业务状态。</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>进入可恢复删除期的时间，非空即视为已删除。</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最后更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>PostgreSQL xmin 并发令牌，用于乐观并发控制。</summary>
    public uint RowVersion { get; set; }

    /// <summary>Source 修订历史。</summary>
    public List<SourceRevision> SourceRevisions { get; set; } = [];

    /// <summary>当前 Source 使用的附件。</summary>
    public List<EventMediaAsset> MediaAssets { get; set; } = [];

    /// <summary>该 Event 的不可变语义分析历史。</summary>
    public List<EventSemanticRun> SemanticRuns { get; set; } = [];

    /// <summary>
    /// 创建新 Event。Trace 默认已完成，Plan 默认待执行。
    /// </summary>
    public static Event Create(
        long userId,
        EventKind kind,
        string? title,
        string? rawContent,
        DateTimeOffset? happenedAt,
        DateTimeOffset? plannedAt,
        string timezone,
        string? idempotencyKey,
        DateTimeOffset now)
    {
        return new Event
        {
            UserId = userId,
            EventKind = kind,
            Status = kind == EventKind.Trace ? EventStatus.Completed : EventStatus.Planned,
            Title = title,
            RawContent = rawContent,
            HappenedAt = happenedAt,
            PlannedAt = plannedAt,
            Timezone = timezone,
            Visibility = Visibility.Private,
            IdempotencyKey = idempotencyKey,
            CurrentSourceRevision = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
