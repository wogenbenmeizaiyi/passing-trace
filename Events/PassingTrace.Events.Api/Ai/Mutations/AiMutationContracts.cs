using PassingTrace.Events.Api.Ai.Assistant;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PassingTrace.Events.Api.Ai.Mutations;

public sealed record AiRecordPatch(
    [property: MaxLength(512)] string? Title = null,
    [property: MaxLength(8000)] string? RawContent = null,
    DateTimeOffset? HappenedAt = null,
    DateTimeOffset? PlannedAt = null,
    IReadOnlyList<string>? ClearFields = null,
    IReadOnlyList<Guid>? SubjectIds = null);

public sealed record AiNewPlanInput(
    [property: Required, StringLength(512, MinimumLength = 1)] string Title,
    DateTimeOffset? PlannedAt = null,
    [property: MaxLength(2000)] string? RawContent = null);

public sealed record AiStorylineNodeInput(long? EventId = null, AiNewPlanInput? NewPlan = null);

public sealed record AiStorylineChange(
    [property: Required, RegularExpression("^(update-metadata|add-existing-event|add-plan|move-node-to-stage|sync-node|remove-node|remove-node-and-reconnect)$")] string Operation,
    Guid? NodeKey = null, long? EventId = null, AiNewPlanInput? NewPlan = null,
    Guid? StageKey = null, int? SemanticOrder = null, Guid? ParentNodeKey = null,
    [property: MaxLength(120)] string? Title = null,
    [property: MaxLength(2000)] string? Description = null,
    string? CategoryKey = null,
    [property: RegularExpression("^(Ongoing|Completed)$")] string? Status = null);

public sealed record AiMutationTarget(string Type, string Id, string Title, int Revision, Guid? SubjectId = null);
public sealed record AiMutationResult(Guid OperationId, string Operation, string State,
    IReadOnlyList<AiMutationTarget> Targets, AiMessageResponse Message);
public sealed record AiApprovalRequest(Guid Id, Guid ConversationId, string TargetType, string TargetId,
    string Title, string Description, DateTimeOffset ExpiresAt);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiApprovalDecisionRequest([Required, RegularExpression("^(confirm|cancel)$")] string Decision);
public sealed record AiApprovalDecisionResponse(Guid Id, string State, AiMutationResult Result);
