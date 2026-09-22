using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using PassingTrace.Identity.AuthorizationServer.Development;
using PassingTrace.Identity.Domain.Entities;
using PassingTrace.Identity.Domain.Enums;

namespace PassingTrace.Identity.AuthorizationServer.Controllers;

/// <summary>Local-only fixtures. No password, account identifier or provisioning API is exposed in production.</summary>
[ApiController, Route("api/v1/development/people")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class DevelopmentPeopleController(UserManager<User> users, IHostEnvironment environment,
    IOptions<DevelopmentAutoLoginOptions> options, TimeProvider clock) : ControllerBase
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var config = options.Value;
        if (!environment.IsDevelopment() || !config.Enabled || string.IsNullOrWhiteSpace(config.Password)) return NotFound();
        var owner = await users.FindByIdAsync(User.FindFirstValue("sub") ?? "");
        if (!User.HasScope("profile") || owner is null || owner.UserName != config.Username || owner.Status != UserStatus.Active) return Forbid();
        await Gate.WaitAsync(ct);
        try
        {
            var result = new List<PersonProfile>();
            foreach (var (suffix, nickname) in new[] { ("friend", "小林 · 演示好友"), ("peer", "小周 · 演示好友"), ("outsider", "未授权用户 · 演示") })
            {
                var name = $"{config.Username}-{suffix}";
                var user = await users.FindByNameAsync(name);
                if (user is null)
                {
                    user = new User
                    {
                        UserName = name,
                        Nickname = nickname,
                        Bio = "仅供本地开发体验的示例账号",
                        Status = UserStatus.Active,
                        CreatedAt = clock.GetUtcNow(),
                        UpdatedAt = clock.GetUtcNow()
                    };
                    if (!(await users.CreateAsync(user, config.Password)).Succeeded) return Conflict(new { message = "演示账号创建失败，请检查本地配置。" });
                    if (!(await users.AddClaimAsync(user, new Claim("development_social_owner", owner.Id.ToString()))).Succeeded)
                        return Conflict(new { message = "演示账号未能完成初始化。" });
                }
                if (!(await users.GetClaimsAsync(user)).Any(c => c.Type == "development_social_owner" && c.Value == owner.Id.ToString()))
                    return Conflict(new { message = "同名账号已存在，未对其进行修改。" });
                result.Add(new(user.Id.ToString(), user.Nickname ?? nickname, user.Bio ?? "", user.AvatarKey != null, user.FriendCode));
            }
            Response.Headers.CacheControl = "private, no-store";
            return Ok(result);
        }
        finally { Gate.Release(); }
    }
}
