using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai.Tools.Mutations;

namespace PassingTrace.Events.Api.Ai.Capabilities;

public sealed class PersonalMutationsCapabilityPackage(PersonalMutationTools tools) : IAiCapabilityPackage
{
    public string Key => "personal-mutations";
    public bool IsAvailable => true;
    public IReadOnlyList<string> Capabilities { get; } = ["create-records", "edit-records", "create-storylines", "edit-storylines", "request-deletion",
        "query-subjects", "subject-timelines", "create-subjects", "edit-subjects", "subject-relations", "subject-entries", "subject-lifecycle"];
    public IReadOnlyList<string> WriteTools { get; } = ["CreateMyRecord", "UpdateMyRecord", "CreateMyStoryline", "UpdateMyStoryline", "RequestDeleteMyRecord", "RequestDeleteMyStoryline",
        "CreateMySubject", "UpdateMySubject", "RelateMySubjects", "UpdateMySubjectRelation", "CreateMySubjectEntry", "UpdateMySubjectEntry", "DecideMySubjectPlan", "UpdateMySubjectLifecycle", "RequestDeleteMySubjectContent"];
    public IReadOnlyList<AITool> CreateTools() =>
    [
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.CreateMyRecordAsync), "CreateMyRecord"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.UpdateMyRecordAsync), "UpdateMyRecord"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.CreateMyStorylineAsync), "CreateMyStoryline"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.UpdateMyStorylineAsync), "UpdateMyStoryline"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.RequestDeleteMyRecordAsync), "RequestDeleteMyRecord"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.RequestDeleteMyStorylineAsync), "RequestDeleteMyStoryline"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.QueryMySubjectsAsync), "QueryMySubjects"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.QueryMySubjectTimelineAsync), "QueryMySubjectTimeline"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.QuerySubjectFieldPresetsAsync), "QuerySubjectFieldPresets"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.CreateMySubjectAsync), "CreateMySubject"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.UpdateMySubjectAsync), "UpdateMySubject"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.RelateMySubjectsAsync), "RelateMySubjects"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.UpdateMySubjectRelationAsync), "UpdateMySubjectRelation"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.CreateMySubjectEntryAsync), "CreateMySubjectEntry"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.UpdateMySubjectEntryAsync), "UpdateMySubjectEntry"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.DecideMySubjectPlanAsync), "DecideMySubjectPlan"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.PreviewMySubjectLifecycleAsync), "PreviewMySubjectLifecycle"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.UpdateMySubjectLifecycleAsync), "UpdateMySubjectLifecycle"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.RequestDeleteMySubjectContentAsync), "RequestDeleteMySubjectContent"),
    ];
}
