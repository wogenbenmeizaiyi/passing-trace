namespace PassingTrace.Core.Subjects;

public enum SubjectKind { Person, Pet, Item }
public enum SubjectState { Active, Ended }
public enum SubjectEntryKind { Record, Plan }
public enum SubjectEntryState { Planned, Completed, Cancelled }

/// <summary>A private dossier. UserId is the data owner, not the subject of its activities.</summary>
public sealed class Subject
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public bool IsSelf { get; set; }
    public SubjectKind Kind { get; set; }
    public string? ItemType { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Timezone { get; set; } = "UTC";
    public SubjectState State { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public string FieldsJson { get; set; } = "[]";
    public string ValuesJson { get; set; } = "{}";
    public string MediaIdsJson { get; set; } = "[]";
    public Guid? CoverMediaId { get; set; }
    public int Revision { get; set; } = 1;
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Only dossier IDs can be endpoints; entries and events never form graph edges.</summary>
public sealed class SubjectRelation
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public Guid FromSubjectId { get; set; }
    public Guid ToSubjectId { get; set; }
    public string Label { get; set; } = "关联";
    public bool Directed { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Independent from Event and social participation, even when another dossier reads it.</summary>
public sealed class SubjectEntry
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public Guid SubjectId { get; set; }
    public SubjectEntryKind Kind { get; set; }
    public SubjectEntryState State { get; set; }
    public string Title { get; set; } = "";
    public string? Content { get; set; }
    public DateTimeOffset? HappenedAt { get; set; }
    public DateTimeOffset? PlannedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string Timezone { get; set; } = "UTC";
    public string FieldChangesJson { get; set; } = "{}";
    public string ActualFieldChangesJson { get; set; } = "{}";
    public string FieldDefinitionsJson { get; set; } = "[]";
    public string MediaIdsJson { get; set; } = "[]";
    public int Revision { get; set; } = 1;
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class SubjectTimelineReference
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public Guid SubjectId { get; set; }
    public long? EventId { get; set; }
    public Events.Event? Event { get; set; }
    public Guid? EntryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

public sealed class SubjectHistory
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    public int Revision { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SubjectMilestone
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public Guid SubjectId { get; set; }
    public string Operation { get; set; } = "";
    public string? Reason { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
}

/// <summary>Explicit media references cover current and historical attachments, including GLB.</summary>
public sealed class SubjectMediaReference
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    public Guid MediaId { get; set; }
    public int Revision { get; set; }
}
