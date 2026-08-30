using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Identity.Application.Accounts;
using PassingTrace.Identity.Domain.Entities;

namespace PassingTrace.Identity.Infrastructure.DependencyInjection;

/// <summary>集中注册 Identity、EF Core、PostgreSQL 和登录 Cookie 的基础设施配置。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册身份基础设施。开发环境允许 HTTP Cookie，非开发环境强制 Secure Cookie。
    /// </summary>
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        var connectionString = configuration.GetConnectionString("identity")
            ?? throw new InvalidOperationException(
                "缺少名为 'identity' 的数据库连接字符串。");

        services.AddDbContext<IdentityDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            // 把 OpenIddict 的客户端、授权、Scope 和 Token 实体加入同一 DbContext。
            options.UseOpenIddict<long>();
        });

        services
            .AddIdentity<User, IdentityRole<long>>(options =>
            {
                options.User.AllowedUserNameCharacters = UsernamePolicy.AllowedCharacters;
                options.User.RequireUniqueEmail = false;

                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;

                // PasswordSignInAsync 的失败计数达到阈值后锁定账号 15 分钟。
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.SignIn.RequireConfirmedAccount = false;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedPhoneNumber = false;
            })
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddUserValidator<IdentityUsernameValidator>();

        services.ConfigureApplicationCookie(options =>
        {
            // Cookie 只维持 Identity 站点的浏览器 SSO 会话，不是业务 API 凭据。
            options.Cookie.Name = "PassingTrace.Identity";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = isDevelopment
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.LoginPath = "/account/login";
            options.AccessDeniedPath = "/account/access-denied";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        return services;
    }
}
