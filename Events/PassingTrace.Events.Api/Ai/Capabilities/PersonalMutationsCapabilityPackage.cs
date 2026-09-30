using Microsoft.Extensions.AI;
using PassingTrace.Events.Api.Ai.Tools.Mutations;

namespace PassingTrace.Events.Api.Ai.Capabilities;

public sealed class PersonalMutationsCapabilityPackage(PersonalMutationTools tools) : IAiCapabilityPackage
{
    public string Key => "personal-mutations";
    public bool IsAvailable => true;
    public IReadOnlyList<string> Capabilities { get; } = ["create-records", "edit-records", "create-storylines", "edit-storylines", "request-deletion"];
    public IReadOnlyList<string> WriteTools { get; } = ["CreateMyRecord", "UpdateMyRecord", "CreateMyStoryline", "UpdateMyStoryline", "RequestDeleteMyRecord", "RequestDeleteMyStoryline"];
    public IReadOnlyList<AITool> CreateTools() =>
    [
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.CreateMyRecordAsync), "CreateMyRecord"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.UpdateMyRecordAsync), "UpdateMyRecord"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.CreateMyStorylineAsync), "CreateMyStoryline"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.UpdateMyStorylineAsync), "UpdateMyStoryline"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.RequestDeleteMyRecordAsync), "RequestDeleteMyRecord"),
        AiFunctionToolFactory.Create(tools, nameof(PersonalMutationTools.RequestDeleteMyStorylineAsync), "RequestDeleteMyStoryline"),
    ];
}
