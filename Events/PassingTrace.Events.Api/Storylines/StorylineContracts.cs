using PassingTrace.Core.Events;
using PassingTrace.Core.Storylines;

namespace PassingTrace.Events.Api.Storylines;

public sealed record StorylineStageInput(Guid Key, string Title, int SemanticOrder);

public sealed record InlinePlanInput(
    string Title,
    DateTimeOffset? PlannedAt,
    string? RawContent,
    string? Timezone);

public sealed record StorylineNodeInput(
    Guid Key,
    string NodeType,
    long? EventId,
    int? SourceRevision,
    InlinePlanInput? NewPlan,
    Guid? StageKey,
    int SemanticOrder,
    StorylineNodeEmphasis Emphasis = StorylineNodeEmphasis.Normal);

public sealed record StorylineEdgeInput(
    Guid Key,
    Guid SourceNodeKey,
    Guid TargetNodeKey,
    StorylineRelationType RelationType,
    string? Label);

public sealed record StorylineWebNodeLayoutInput(
    Guid NodeKey,
    decimal X,
    decimal Y,
    decimal? Width,
    decimal? Height);

public sealed record StorylineWebStageLayoutInput(
    Guid StageKey,
    decimal X,
    decimal Y,
    decimal Width,
    decimal Height);

public sealed record StorylineWebLayoutInput(
    string Direction,
    decimal ViewportX,
    decimal ViewportY,
    decimal Zoom,
    IReadOnlyList<StorylineWebNodeLayoutInput>? Nodes,
    IReadOnlyList<StorylineWebStageLayoutInput>? Stages);

public sealed record SaveStorylineRequest(
    string Title,
    string? Description,
    string CategoryKey,
    StorylineStatus Status,
    Guid? CoverMediaAssetId,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<StorylineStageInput>? Stages,
    IReadOnlyList<StorylineNodeInput>? Nodes,
    IReadOnlyList<StorylineEdgeInput>? Edges,
    StorylineWebLayoutInput? WebCanvasLayout);

public sealed record StorylineChangeRequest(
    string Operation,
    Guid? NodeKey = null,
    long? EventId = null,
    int? SourceRevision = null,
    InlinePlanInput? NewPlan = null,
    Guid? StageKey = null,
    int? SemanticOrder = null,
    StorylineNodeEmphasis? Emphasis = null,
    Guid? ParentNodeKey = null,
    bool CreateBranch = false,
    string? Title = null,
    string? Description = null,
    string? CategoryKey = null,
    StorylineStatus? Status = null,
    IReadOnlyList<string>? Tags = null);

public sealed record StorylineListResponse(IReadOnlyList<StorylineSummaryResponse> Items, Guid? NextCursor);

public sealed record StorylineSummaryResponse(
    Guid Id,
    string Title,
    string? Description,
    string CategoryKey,
    string CategoryLabel,
    StorylineStatus Status,
    int Revision,
    uint Version,
    Guid? CoverMediaAssetId,
    DateTimeOffset? RangeStart,
    DateTimeOffset? RangeEnd,
    int NodeCount,
    IReadOnlyList<string> Tags,
    StorylineLayoutState LayoutState,
    DateTimeOffset UpdatedAt);

public sealed record StorylineNodeResponse(
    Guid Key,
    long EventId,
    int SourceRevision,
    int CurrentSourceRevision,
    string RevisionState,
    EventKind Kind,
    EventStatus Status,
    string Title,
    string? RawContent,
    DateTimeOffset? OccurredAt,
    Guid? StageKey,
    int SemanticOrder,
    StorylineNodeEmphasis Emphasis,
    string? Place,
    IReadOnlyList<string> Tags,
    Guid? ImageMediaAssetId);

public sealed record StorylineOutlineNodeResponse(
    Guid NodeKey,
    Guid? StageKey,
    int TopologicalOrder,
    int Depth,
    int IncomingCount,
    int OutgoingCount,
    bool StartsBranch,
    bool IsMerge);

public sealed record StorylineRevisionResponse(
    Guid Id,
    string Title,
    string? Description,
    string CategoryKey,
    string CategoryLabel,
    StorylineStatus Status,
    int Revision,
    uint Version,
    Guid? CoverMediaAssetId,
    DateTimeOffset? RangeStart,
    DateTimeOffset? RangeEnd,
    StorylineLayoutState LayoutState,
    IReadOnlyList<string> Tags,
    IReadOnlyList<StorylineStageInput> Stages,
    IReadOnlyList<StorylineNodeResponse> Nodes,
    IReadOnlyList<StorylineEdgeInput> Edges,
    IReadOnlyList<StorylineOutlineNodeResponse> Outline,
    StorylineWebLayoutInput? WebCanvasLayout,
    DateTimeOffset UpdatedAt);

public sealed record StorylineSaveResponse(
    StorylineRevisionResponse Storyline,
    IReadOnlyDictionary<Guid, long> CreatedPlans,
    int? UndoRevision);

public sealed record StorylineRevisionHistoryResponse(
    int Revision,
    string ContentHash,
    StorylineLayoutState LayoutState,
    int NodeCount,
    DateTimeOffset CreatedAt,
    bool IsCurrent);

public sealed record StorylineTaxonomyResponse(
    string Version,
    IReadOnlyList<StorylineCategoryResponse> Categories,
    IReadOnlyList<StorylineRelationResponse> Relations);

public sealed record StorylineCategoryResponse(string Key, string Label);
public sealed record StorylineRelationResponse(StorylineRelationType Value, string Key, string Label);
