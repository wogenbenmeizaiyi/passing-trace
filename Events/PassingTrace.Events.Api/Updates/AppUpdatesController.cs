using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PassingTrace.Events.Api.Updates;

[ApiController]
[AllowAnonymous]
[Route("api/v1/app-updates")]
public sealed class AppUpdatesController(AppUpdateService service) : ControllerBase
{
    [HttpGet("android/latest")]
    [ProducesResponseType<AndroidUpdateResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AndroidUpdateResponse>> GetAndroidLatestAsync(
        [FromQuery] int currentVersionCode,
        CancellationToken cancellationToken)
    {
        if (currentVersionCode < 1)
        {
            ModelState.AddModelError(nameof(currentVersionCode), "currentVersionCode 必须大于 0。");
            return ValidationProblem(ModelState);
        }

        return Ok(await service.GetAndroidUpdateAsync(currentVersionCode, cancellationToken));
    }
}
