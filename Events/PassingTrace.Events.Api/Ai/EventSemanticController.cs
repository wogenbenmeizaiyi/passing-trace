using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassingTrace.Events.Api.Security;

namespace PassingTrace.Events.Api.Ai;

[ApiController]
[Authorize]
[Route("api/v1/events/{eventId:long}/semantic")]
public sealed class EventSemanticController(EventSemanticService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EventSemanticResponse>> GetAsync(long eventId, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(User.GetUserId(), eventId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("reparse")]
    public async Task<IActionResult> ReparseAsync(long eventId, CancellationToken cancellationToken) =>
        await service.ReparseAsync(User.GetUserId(), eventId, cancellationToken) ? Accepted() : NotFound();
}
