using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PassingTrace.Identity.AuthorizationServer.Mobile;

namespace PassingTrace.Identity.AuthorizationServer.Controllers;

[ApiController]
[Route("api/mobile")]
public sealed class MobileAccountController(MobileFlowService mobileFlow) : ControllerBase
{
    [HttpPost("registration-intents")]
    [EnableRateLimiting("mobile-registration")]
    public async Task<IActionResult> CreateRegistrationIntent(
        CreateRegistrationIntentRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => mobileFlow.CreateRegistrationIntentAsync(request, cancellationToken));

    [HttpPost("registrations")]
    [EnableRateLimiting("mobile-registration")]
    public async Task<IActionResult> Register(
        CompleteRegistrationRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            () => mobileFlow.CompleteRegistrationAsync(request, GetPublicOrigin(), cancellationToken),
            created: true);

    [HttpPost("authorization-launches")]
    [EnableRateLimiting("mobile-launch")]
    public async Task<IActionResult> CreateAuthorizationLaunch(
        CreateAuthorizationLaunchRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            () => mobileFlow.CreateAuthorizationLaunchAsync(request, GetPublicOrigin(), cancellationToken));

    private Uri GetPublicOrigin()
    {
        var configured = HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["OpenIddict:Issuer"];
        return !string.IsNullOrWhiteSpace(configured)
            ? new Uri(configured, UriKind.Absolute)
            : new Uri($"{Request.Scheme}://{Request.Host}{Request.PathBase}/", UriKind.Absolute);
    }

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action, bool created = false)
    {
        try
        {
            var result = await action();
            return created ? StatusCode(StatusCodes.Status201Created, result) : Ok(result);
        }
        catch (MobileFlowException exception)
        {
            return Problem(
                statusCode: exception.StatusCode,
                title: exception.Code,
                detail: exception.Message);
        }
    }
}
