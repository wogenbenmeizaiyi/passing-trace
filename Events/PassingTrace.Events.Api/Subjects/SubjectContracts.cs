using System.Text.Json;
using PassingTrace.Core.Subjects;

namespace PassingTrace.Events.Api.Subjects;

public sealed record SubjectField(Guid Id, string Name, string Type, string? Unit = null,
    IReadOnlyList<string>? Options = null, string? Key = null, bool Removed = false);
public sealed record SubjectRelationInput(Guid ToSubjectId, string Label = "关联", bool Directed = false,
    DateTimeOffset? StartedAt = null, DateTimeOffset? EndedAt = null);
public sealed record CreateSubjectRequest(SubjectKind Kind, string Name, IReadOnlyList<SubjectRelationInput> Relations,
    string? ItemType = null, string? Description = null, DateTimeOffset? StartedAt = null, string? Timezone = null,
    IReadOnlyList<SubjectField>? Fields = null, Dictionary<string, JsonElement>? Values = null,
    IReadOnlyList<Guid>? MediaIds = null, Guid? CoverMediaId = null);
public sealed record UpdateSubjectRequest(string? Name = null, string? Description = null,
    IReadOnlyList<SubjectField>? Fields = null, Dictionary<string, JsonElement>? Values = null,
    IReadOnlyList<Guid>? MediaIds = null, Guid? CoverMediaId = null, bool ClearCover = false,
    DateTimeOffset? EffectiveAt = null);
public sealed record UpdateSubjectRelationRequest(string? Label = null, bool? Directed = null,
    DateTimeOffset? StartedAt = null, DateTimeOffset? EndedAt = null, bool Resume = false,
    Guid? FromSubjectId = null, Guid? ToSubjectId = null);
public sealed record SubjectEntryRequest(SubjectEntryKind? Kind = null, string? Title = null, string? Content = null,
    DateTimeOffset? HappenedAt = null, DateTimeOffset? PlannedAt = null, string? Timezone = null,
    Dictionary<string, JsonElement>? FieldChanges = null, IReadOnlyList<Guid>? MediaIds = null,
    IReadOnlyList<Guid>? MarkedSubjectIds = null, bool ClearPlannedAt = false);
public sealed record SubjectEntryDecision(string Operation, DateTimeOffset? HappenedAt = null,
    Dictionary<string, JsonElement>? ActualFieldChanges = null);
public sealed record SubjectLifecycleRequest(string Operation, DateTimeOffset EffectiveAt, string? Reason = null,
    string? Note = null, IReadOnlyList<SubjectPlanVersion>? CancelPlans = null, Guid? CorrectsId = null, bool UndoEnd = false);
public sealed record SubjectPlanVersion(Guid Id, int Version);
public sealed record SubjectResponse(Guid Id, SubjectKind Kind, string? ItemType, string Name, string? Description,
    bool IsSelf, SubjectState State, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, string? EndReason,
    int Version, string Timezone, IReadOnlyList<SubjectField> Fields, Dictionary<string, JsonElement> Values,
    IReadOnlyList<Guid> MediaIds, Guid? CoverMediaId, DateTimeOffset UpdatedAt, int? AgeDays);
public sealed record SubjectEntryResponse(Guid Id, Guid SubjectId, string SubjectName, bool SourceSubjectDeleted,
    SubjectEntryKind Kind, SubjectEntryState State, string Title, string? Content, DateTimeOffset? HappenedAt,
    DateTimeOffset? PlannedAt, DateTimeOffset? CompletedAt, string Timezone, int Version,
    Dictionary<string, JsonElement> FieldChanges, Dictionary<string, JsonElement> ActualFieldChanges,
    IReadOnlyList<SubjectField> FieldDefinitions, IReadOnlyList<Guid> MediaIds, IReadOnlyList<Guid> MarkedSubjectIds,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SubjectGraphResponse(Guid RootId, IReadOnlyList<SubjectResponse> Nodes, IReadOnlyList<SubjectRelation> Relations);
public sealed record SubjectTimelineItem(string SourceType, string SourceId, string Kind, string State,
    string Title, string? Content, DateTimeOffset? OccurredAt, DateTimeOffset CreatedAt, Guid? OriginSubjectId,
    string? OriginSubjectName, bool IsReference, bool Invalid, bool AfterEnd, int Version,
    IReadOnlyList<Guid> MediaIds, Dictionary<string, JsonElement>? FieldChanges = null,
    IReadOnlyList<SubjectField>? FieldDefinitions = null);
public sealed record SubjectTimelineGroup(string Key, IReadOnlyList<SubjectTimelineItem> Items);
public sealed record SubjectTimelineResponse(IReadOnlyList<SubjectTimelineGroup> Groups, string? NextCursor, string Timezone);
public sealed record SubjectLifecyclePreview(SubjectResponse Subject, IReadOnlyList<SubjectEntryResponse> Plans, IReadOnlyList<SubjectMilestone> Milestones);
