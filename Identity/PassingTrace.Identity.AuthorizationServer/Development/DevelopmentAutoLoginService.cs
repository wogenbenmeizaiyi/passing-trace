using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PassingTrace.Identity.Application.Accounts;
using PassingTrace.Identity.Domain.Entities;
using PassingTrace.Identity.Domain.Enums;

namespace PassingTrace.Identity.AuthorizationServer.Development;

/// <summary>
/// 后端开发阶段的 Web 自动登录：当客户端不是移动端且未建立会话时，
/// 用固定账号直接建立 Identity Cookie，跳过扫码页面。
/// 账号不存在时自动创建；只在配置开启且处于开发/测试环境时生效。
/// </summary>
public sealed class DevelopmentAutoLoginService(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    IOptions<DevelopmentAutoLoginOptions> options,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<DevelopmentAutoLoginService> logger)
{
    private readonly DevelopmentAutoLoginOptions _options = options.Value;

    public async Task<User?> TryLoginAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return null;
        }

        if (!UsernamePolicy.IsValid(_options.Username) ||
            string.IsNullOrWhiteSpace(_options.Password))
        {
            logger.LogWarning("DevelopmentAutoLogin 配置无效，已跳过自动登录。");
            return null;
        }

        var username = _options.Username!;
        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            var now = timeProvider.GetUtcNow();
            user = new User
            {
                UserName = username,
                Status = UserStatus.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            var created = await userManager.CreateAsync(user, _options.Password!);
            if (!created.Succeeded)
            {
                logger.LogWarning(
                    "自动登录账号创建失败：{Errors}",
                    string.Join("; ", created.Errors.Select(error => error.Description)));
                return null;
            }

            logger.LogInformation("已为开发自动登录创建固定账号 {Username}。", username);
        }

        if (user.Status != UserStatus.Active)
        {
            return null;
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        logger.LogInformation("开发自动登录：{Username} 已建立会话。", username);
        return user;
    }
}
