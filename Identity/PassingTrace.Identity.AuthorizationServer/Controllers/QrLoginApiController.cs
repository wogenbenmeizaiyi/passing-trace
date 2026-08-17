using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Validation.AspNetCore;
using PassingTrace.Identity.AuthorizationServer.QrLogin;
using PassingTrace.Identity.AuthorizationServer.Setup;
using PassingTrace.Identity.Domain.Enums;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace PassingTrace.Identity.AuthorizationServer.Controllers;

[ApiController]
[Route("api/qr-login/transactions")]
[Authorize(
    AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
    Policy = "mobile-login-approval")]
[EnableRateLimiting("qr-approval")]
public sealed class QrLoginApiController(
    QrLoginService qrLogin,
    FirstPartyClientRegistry clients) : ControllerBase
{
    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken cancellationToken)
    {
        var transaction = await qrLogin.GetByCodeAsync(code, cancellationToken);
        if (transaction is null)
        {
            return NotFound();
        }

        var client = clients.GetRequired(transaction.ClientId);
        return Ok(new
        {
            transaction.ClientId,
            ClientDisplayName = client.DisplayName,
            RequestedScopes = new[] { Scopes.OpenId, Scopes.Profile, IdentityOpenIddictConstants.ApiScope },
            Browser = transaction.UserAgent,
            transaction.SourceIp,
            Location = (string?)null,
            transaction.CreatedAt,
            transaction.ExpiresAt,
            Status = transaction.Status.ToString().ToLowerInvariant()
        });
    }

    [HttpPost("{code}/approve")]
    public Task<IActionResult> Approve(string code, CancellationToken cancellationToken) =>
        DecideAsync(code, approve: true, cancellationToken);

    [HttpPost("{code}/reject")]
    public Task<IActionResult> Reject(string code, CancellationToken cancellationToken) =>
        DecideAsync(code, approve: false, cancellationToken);

    private async Task<IActionResult> DecideAsync(
        string code,
        bool approve,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(Claims.Subject)?.Value;
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var status = await qrLogin.DecideAsync(code, userId, approve, cancellationToken);
            return Ok(new { Status = status.ToString().ToLowerInvariant() });
        }
        catch (QrLoginException exception)
        {
            return Problem(statusCode: exception.StatusCode, title: exception.Code, detail: exception.Message);
        }
    }
}
