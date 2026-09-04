using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PassingTrace.Events.Api.Ai;

[ApiController]
[Authorize]
[Route("api/v1/ai/memories")]
public sealed class UserMemoriesController(UserMemoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserMemoryResponse>>> ListAsync(CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(cancellationToken));

    [HttpPatch("{id:long}")]
    public async Task<ActionResult<UserMemoryResponse>> UpdateAsync(long id, [FromBody] UpdateUserMemoryRequest request,
        CancellationToken cancellationToken) => Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await service.RejectAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAllAsync(CancellationToken cancellationToken)
    {
        await service.RejectAllAsync(cancellationToken);
        return NoContent();
    }
}
