using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using PassingTrace.Identity.Domain.Entities;
using PassingTrace.Identity.Domain.Enums;
using PassingTrace.Identity.AuthorizationServer.Mobile;
using PassingTrace.Identity.AuthorizationServer.QrLogin;
using PassingTrace.Identity.AuthorizationServer.Setup;
using PassingTrace.Identity.AuthorizationServer.Development;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace PassingTrace.Identity.AuthorizationServer.Controllers;

/// <summary>
/// 实现 OpenIddict 透传后的授权、换取令牌和退出端点；协议参数验证由 OpenIddict 完成。
/// </summary>
public sealed class AuthorizationController(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictScopeManager scopeManager,
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    FirstPartyClientRegistry clients,
    MobileFlowService mobileFlow,
    QrLoginService qrLogin,
    DevelopmentAutoLoginService developmentAutoLogin) : Controller
{
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    /// <summary>使用 Identity Cookie 建立授权主体并签发一次性授权码。</summary>
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "无法读取 OpenID Connect 授权请求。");

        var authentication = await HttpContext.AuthenticateAsync();
        User? user = null;

        var handoffCode = request.GetParameter("handoff_code").ToString();
        if (!string.IsNullOrWhiteSpace(handoffCode))
        {
            if (!clients.IsMobile(request.ClientId!))
            {
                return ForbidWithError(Errors.InvalidRequest, "交接码只能用于移动客户端。");
            }

            user = await mobileFlow.ConsumeHandoffAsync(handoffCode, HttpContext.RequestAborted);
            if (user is null)
            {
                return ForbidWithError(Errors.InvalidRequest, "移动注册交接码无效或已过期。");
            }
            await signInManager.SignInAsync(user, isPersistent: false);
        }

        if (user is null && authentication is { Succeeded: true } &&
            !request.HasPromptValue(PromptValues.Login))
        {
            user = await userManager.GetUserAsync(authentication.Principal);
        }

        // 未登录时，移动客户端进入受设备票据保护的密码页；其他客户端进入扫码页。
        if (user is null)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return ForbidWithError(
                    Errors.LoginRequired,
                    "用户尚未登录。");
            }

            var authorizeRequest = Request.PathBase + Request.Path +
                QueryString.Create(
                    Request.HasFormContentType ? Request.Form : Request.Query);

            // 后端开发阶段：非移动客户端在配置开启时用固定账号自动登录，跳过扫码。
            if (!clients.IsMobile(request.ClientId!))
            {
                user = await developmentAutoLogin.TryLoginAsync(
                    HttpContext.RequestAborted);
            }

            if (user is null)
            {
                if (!clients.IsMobile(request.ClientId!))
                {
                    var created = await qrLogin.CreateAsync(
                        request.ClientId!,
                        authorizeRequest,
                        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        Request.Headers.UserAgent.ToString(),
                        HttpContext.RequestAborted);
                    var cookieName = QrLoginService.CookieName(created.Id);
                    Response.Cookies.Append(cookieName, created.BrowserBinding, new CookieOptions
                    {
                        HttpOnly = true,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = Request.IsHttps,
                        Expires = created.ExpiresAt,
                        Path = $"/account/qr-login/{created.Id}"
                    });
                    return Redirect($"/account/qr-login/{created.Id}?code={Uri.EscapeDataString(created.Code)}");
                }

                var launchTicket = request.GetParameter("launch_ticket").ToString();
                if (!await mobileFlow.IsValidLoginLaunchAsync(
                    launchTicket,
                    HttpContext.RequestAborted))
                {
                    return ForbidWithError(Errors.InvalidRequest, "缺少有效的移动设备启动票据。");
                }

                return Challenge(new AuthenticationProperties
                {
                    RedirectUri = authorizeRequest
                });
            }
        }

        if (user.Status != UserStatus.Active ||
            !await signInManager.CanSignInAsync(user))
        {
            await signInManager.SignOutAsync();
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = Request.PathBase + Request.Path + Request.QueryString
            });
        }

        var application = await applicationManager.FindByClientIdAsync(
            request.ClientId!)
            ?? throw new InvalidOperationException("未找到请求中的客户端。");

        var subject = await userManager.GetUserIdAsync(user);
        var applicationId = await applicationManager.GetIdAsync(application)
            ?? throw new InvalidOperationException("客户端缺少内部标识。");

        // 第一方客户端跳过 consent，并复用同一用户、客户端、Scope 的永久授权记录。
        var authorizations = await authorizationManager.FindAsync(
            subject: subject,
            client: applicationId,
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes()).ToListAsync();

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);

        identity.SetClaim(Claims.Subject, subject)
            .SetClaim(Claims.Name, user.UserName)
            .SetClaim(Claims.PreferredUsername, user.UserName);

        identity.SetScopes(request.GetScopes());
        // Scope 映射出的 Resource 会成为 access token 的 aud。
        identity.SetResources(
            await scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());

        var authorization = authorizations.LastOrDefault()
            ?? await authorizationManager.CreateAsync(
                identity,
                subject,
                applicationId,
                AuthorizationTypes.Permanent,
                identity.GetScopes());

        identity.SetAuthorizationId(
            await authorizationManager.GetIdAsync(authorization));
        identity.SetDestinations(GetDestinations);

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    /// <summary>使用授权码或 Refresh Token 换取新的 JWT Access Token。</summary>
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "无法读取 OpenID Connect Token 请求。");

        if (!request.IsAuthorizationCodeGrantType() &&
            !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("不支持指定的授权类型。");
        }

        var authentication = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = authentication.Principal?.GetClaim(Claims.Subject);
        var user = subject is null
            ? null
            : await userManager.FindByIdAsync(subject);

        // 每次刷新都重新读取用户状态，停用账号不能依靠旧 Refresh Token 续期。
        if (user is null || user.Status != UserStatus.Active ||
            !await signInManager.CanSignInAsync(user))
        {
            return ForbidWithError(
                Errors.InvalidGrant,
                "该授权已失效。");
        }

        var identity = new ClaimsIdentity(
            authentication.Principal!.Claims,
            TokenValidationParameters.DefaultAuthenticationType,
            Claims.Name,
            Claims.Role);

        identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user))
            .SetClaim(Claims.Name, user.UserName)
            .SetClaim(Claims.PreferredUsername, user.UserName);
        identity.SetDestinations(GetDestinations);

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/logout")]
    /// <summary>显示退出确认页面。</summary>
    public IActionResult Logout() => View();

    [ActionName(nameof(Logout))]
    [HttpPost("~/connect/logout")]
    [ValidateAntiForgeryToken]
    /// <summary>清除 Identity Cookie，并让 OpenIddict 完成 OIDC 退出响应。</summary>
    public async Task<IActionResult> LogoutPost()
    {
        await signInManager.SignOutAsync();

        var request = HttpContext.GetOpenIddictServerRequest();
        // 该 URI 已由 OpenIddict 按客户端白名单验证；未提供时才返回 Identity 首页。
        var redirectUri = request?.PostLogoutRedirectUri ?? "/";

        return SignOut(
            new AuthenticationProperties { RedirectUri = redirectUri },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private ForbidResult ForbidWithError(string error, string description) =>
        Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        // OpenIddict 默认不会盲目把所有 Claim 放入 Token，必须逐项指定目的地。
        if (claim.Type is Claims.Name or Claims.PreferredUsername)
        {
            yield return Destinations.AccessToken;

            if (claim.Subject!.HasScope(Scopes.Profile))
            {
                yield return Destinations.IdentityToken;
            }

            yield break;
        }

        if (claim.Type != "AspNet.Identity.SecurityStamp")
        {
            yield return Destinations.AccessToken;
        }
    }
}
