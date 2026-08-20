using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using PassingTrace.Identity.AuthorizationServer.Development;
using PassingTrace.Identity.AuthorizationServer.Mobile;
using PassingTrace.Identity.AuthorizationServer.QrLogin;
using PassingTrace.Identity.AuthorizationServer.Setup;

namespace PassingTrace.Identity.AuthorizationServer.DependencyInjection;

/// <summary>注册协议之外的身份服务、授权策略和客户端初始化。</summary>
public static class ApplicationServicesExtensions
{
    /// <summary>
    /// 注册协议之外的身份服务：客户端/Scope 初始化、移动与扫码服务、
    /// MVC/Razor 页面以及手机扫码批准授权策略。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">用于读取 <c>MobileRegistration</c> 与 <c>QrLogin</c> 配置节。</param>
    public static IServiceCollection AddIdentityApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHostedService<OpenIddictSeeder>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<FirstPartyClientRegistry>();
        services.Configure<MobileRegistrationOptions>(
            configuration.GetSection(MobileRegistrationOptions.SectionName));
        services.Configure<QrLoginOptions>(
            configuration.GetSection(QrLoginOptions.SectionName));
        services.Configure<DevelopmentAutoLoginOptions>(
            configuration.GetSection(DevelopmentAutoLoginOptions.SectionName));
        services.AddScoped<MobileFlowService>();
        services.AddScoped<QrLoginService>();
        services.AddScoped<DevelopmentAutoLoginService>();
        services.AddControllersWithViews();
        services.AddRazorPages();

        services.AddAuthorizationBuilder()
            .AddPolicy("mobile-login-approval", policy =>
            {
                policy.AddAuthenticationSchemes(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    string.Equals(
                        context.User.FindFirst(OpenIddictConstants.Claims.ClientId)?.Value,
                        IdentityOpenIddictConstants.MobileClientId,
                        StringComparison.Ordinal) &&
                    context.User.HasScope(IdentityOpenIddictConstants.LoginApprovalScope));
            });

        return services;
    }
}
