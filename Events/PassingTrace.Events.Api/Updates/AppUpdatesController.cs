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
        if (currentVersionCode < 0)
        {
            ModelState.AddModelError(nameof(currentVersionCode), "currentVersionCode 不能小于 0。");
            return ValidationProblem(ModelState);
        }

        return Ok(await service.GetAndroidUpdateAsync(currentVersionCode, cancellationToken));
    }

    [HttpGet("android/latest/download")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> DownloadAndroidLatestAsync(CancellationToken cancellationToken)
    {
        var downloadUrl = await service.GetLatestAndroidDownloadAsync(cancellationToken);
        return Redirect(downloadUrl.AbsoluteUri);
    }
}
