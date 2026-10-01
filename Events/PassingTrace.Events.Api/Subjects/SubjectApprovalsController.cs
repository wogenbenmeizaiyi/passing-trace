using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassingTrace.Core.Events;
using PassingTrace.Events.Api.Ai.Mutations;

namespace PassingTrace.Events.Api.Subjects;

public sealed record SubjectDeleteRequest(string TargetType, Guid TargetId, Guid RequestId);

[ApiController, Authorize, Route("api/v1/subjects/delete-requests")]
public sealed class SubjectApprovalsController(AiMutationService mutations) : ControllerBase
{
    [HttpPost]
    public Task<AiApprovalRequest> RequestAsync(SubjectDeleteRequest request, CancellationToken ct)
    {
        if (request.TargetType is not ("Subject" or "SubjectEntry" or "SubjectRelation") || request.RequestId == Guid.Empty)
            throw new DomainValidationException("删除申请无效。");
        return mutations.RequestManualDeleteAsync(request.TargetType, request.TargetId, request.RequestId, ct);
    }
}
