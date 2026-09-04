namespace PassingTrace.Core.Events;

using PassingTrace.Core.Media;

/// <summary>
/// Source 修订快照。每次修改 Source 追加一条，旧值永不原地覆盖，
/// 以便追溯 AI 语义所依据的事实版本。
/// </summary>
public sealed class SourceRevision
{
    /// <summary>快照主键。</summary>
    public long Id { get; set; }

    /// <summary>所属 Event。</summary>
    public long EventId { get; set; }

    /// <summary>修订版本号，从 1 开始递增。</summary>
    public int Revision { get; set; }

    /// <summary>该版本的标题。</summary>
    public string? Title { get; set; }

    /// <summary>该版本的用户原始内容。</summary>
    public string? RawContent { get; set; }

    /// <summary>该版本的实际发生时间。</summary>
    public DateTimeOffset? HappenedAt { get; set; }

    /// <summary>该版本的计划发生时间。</summary>
    public DateTimeOffset? PlannedAt { get; set; }

    /// <summary>快照创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>所属 Event 导航。</summary>
    public Event Event { get; set; } = null!;

    /// <summary>该修订对应的附件快照。</summary>
    public List<SourceRevisionMedia> MediaAssets { get; set; } = [];

    public List<SourceRevisionLabel> Labels { get; set; } = [];

    public List<EventLocation> Locations { get; set; } = [];

    /// <summary>构造一条 Source 修订快照。</summary>
    public static SourceRevision Create(
        long eventId,
        int revision,
        string? title,
        string? rawContent,
        DateTimeOffset? happenedAt,
        DateTimeOffset? plannedAt,
        DateTimeOffset now)
    {
        return new SourceRevision
        {
            EventId = eventId,
            Revision = revision,
            Title = title,
            RawContent = rawContent,
            HappenedAt = happenedAt,
            PlannedAt = plannedAt,
            CreatedAt = now,
        };
    }
}
