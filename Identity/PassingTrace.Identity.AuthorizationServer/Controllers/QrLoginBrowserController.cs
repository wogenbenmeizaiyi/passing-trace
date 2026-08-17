using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PassingTrace.Identity.AuthorizationServer.QrLogin;
using PassingTrace.Identity.AuthorizationServer.Setup;
using PassingTrace.Identity.Domain.Entities;
using QRCoder;

namespace PassingTrace.Identity.AuthorizationServer.Controllers;

[Route("account/qr-login")]
public sealed class QrLoginBrowserController(
    QrLoginService qrLogin,
    FirstPartyClientRegistry clients,
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    IOptions<QrLoginOptions> options) : Controller
{
    [HttpGet("{id:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(Guid id, string code, CancellationToken cancellationToken)
    {
        var transaction = await qrLogin.GetByIdAndCodeAsync(id, code, cancellationToken);
        if (transaction is null)
        {
            return NotFound();
        }

        var origin = string.IsNullOrWhiteSpace(options.Value.PublicOrigin)
            ? $"{Request.Scheme}://{Request.Host}{Request.PathBase}"
            : options.Value.PublicOrigin.TrimEnd('/');
        var payload = $"{origin}/mobile/qr-login?v=1&code={Uri.EscapeDataString(code)}";
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var renderer = new SvgQRCode(data);
        var model = new QrLoginPageModel(
            id,
            renderer.GetGraphic(8),
            clients.GetRequired(transaction.ClientId).DisplayName,
            transaction.ExpiresAt,
            options.Value.PollIntervalSeconds);
        return View(model);
    }

    [HttpGet("{id:guid}/status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status(Guid id, CancellationToken cancellationToken)
    {
        var binding = Request.Cookies[QrLoginService.CookieName(id)];
        var status = await qrLogin.GetStatusAsync(id, binding, cancellationToken);
        return status is null
            ? NotFound()
            : Ok(new { Status = status.Value.ToString().ToLowerInvariant() });
    }

    [HttpPost("{id:guid}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var binding = Request.Cookies[QrLoginService.CookieName(id)];
            var result = await qrLogin.ConsumeAsync(id, binding, cancellationToken);
            var user = await userManager.FindByIdAsync(result.UserId.ToString());
            if (user is null)
            {
                return Forbid();
            }

            await signInManager.SignInAsync(user, isPersistent: false);
            Response.Cookies.Delete(QrLoginService.CookieName(id));
            return LocalRedirect(result.AuthorizeRequest);
        }
        catch (QrLoginException exception)
        {
            return Problem(statusCode: exception.StatusCode, title: exception.Code, detail: exception.Message);
        }
    }
}

public sealed record QrLoginPageModel(
    Guid Id,
    string Svg,
    string ClientDisplayName,
    DateTimeOffset ExpiresAt,
    int PollIntervalSeconds);
