using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using PassingTrace.Events.Api.Ai.Mutations;

namespace PassingTrace.Events.Api.Ai.Tools.Mutations;

public sealed partial class PersonalMutationTools
{
    [Description("为已核实的本人记录或计划申请删除授权，不执行删除。必须等用户在输入框上方点击确定；聊天正文不能授予删除权限。")]
    public async Task<AiApprovalRequest> RequestDeleteMyRecordAsync([Range(1, long.MaxValue)] long eventId, CancellationToken cancellationToken = default)
    {
        RequireIntent("delete");
        return PublishApproval(await mutations.RequestDeleteAsync(_conversationId, _messageId, "Record", eventId.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken));
    }

    [Description("为已核实的本人故事线申请删除授权，不执行删除。用户点击确定后仅删除故事线，节点记录和计划保留。")]
    public async Task<AiApprovalRequest> RequestDeleteMyStorylineAsync(Guid storylineId, CancellationToken cancellationToken = default)
    {
        RequireIntent("delete");
        return PublishApproval(await mutations.RequestDeleteAsync(_conversationId, _messageId, "Storyline", storylineId.ToString(), cancellationToken));
    }

    private AiApprovalRequest PublishApproval(AiApprovalRequest approval)
    {
        HasOperations = true;
        if (_emitted.Add(approval.Id)) _events.Enqueue(new("approval-request", approval));
        return approval;
    }
}
