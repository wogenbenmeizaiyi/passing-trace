using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using PassingTrace.Identity.Infrastructure;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace PassingTrace.Identity.AuthorizationServer.Setup;

/// <summary>
/// 启动时幂等同步固定 Scope 和第一方客户端；配置是声明源，数据库是运行时存储。
/// </summary>
public sealed class OpenIddictSeeder(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        // 集成测试使用内存 SQLite，不执行生产迁移，因此在测试环境直接建表。
        if (environment.IsEnvironment("Testing"))
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<IdentityDbContext>();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        await EnsureScopeAsync(
            scopeManager,
            IdentityOpenIddictConstants.ApiScope,
            "PassingTrace API",
            IdentityOpenIddictConstants.ApiResource,
            cancellationToken);
        await EnsureScopeAsync(
            scopeManager,
            IdentityOpenIddictConstants.LoginApprovalScope,
            "PassingTrace mobile login approval",
            IdentityOpenIddictConstants.IdentityResource,
            cancellationToken);

        var registrations = configuration
            .GetSection("OpenIddict:Clients")
            .Get<OpenIddictClientRegistration[]>() ?? [];

        if (registrations.Length == 0)
        {
            throw new InvalidOperationException(
                "至少需要配置一个 OpenIddict:Clients 客户端。");
        }

        var manager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();

        foreach (var registration in registrations)
        {
            await EnsureClientAsync(manager, registration, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureScopeAsync(
        IOpenIddictScopeManager manager,
        string name,
        string displayName,
        string resource,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = name,
            DisplayName = displayName,
            Resources = { resource }
        };

        var scope = await manager.FindByNameAsync(
            descriptor.Name,
            cancellationToken);

        // 更新已有记录使配置修改可重复部署，不会制造重复 Scope。
        if (scope is null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            await manager.UpdateAsync(scope, descriptor, cancellationToken);
        }
    }

    private static async Task EnsureClientAsync(
        IOpenIddictApplicationManager manager,
        OpenIddictClientRegistration registration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registration.ClientId))
        {
            throw new InvalidOperationException("OpenIddict 客户端缺少 ClientId。");
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = registration.ClientId,
            // Flutter、桌面和 SPA 都无法安全保存 Client Secret，因此统一为公共客户端。
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = registration.DisplayName,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.EndSession,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + IdentityOpenIddictConstants.ApiScope
            },
            Requirements =
            {
                // 每个客户端仍必须用 PKCE 保护授权码，防止授权码被截获后直接兑换。
                Requirements.Features.ProofKeyForCodeExchange
            }
        };

        foreach (var uri in registration.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        foreach (var uri in registration.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        if (string.Equals(registration.LoginMode, "mobile", StringComparison.Ordinal))
        {
            descriptor.Permissions.Add(
                Permissions.Prefixes.Scope + IdentityOpenIddictConstants.LoginApprovalScope);
        }

        var application = await manager.FindByClientIdAsync(
            registration.ClientId,
            cancellationToken);

        if (application is null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            await manager.UpdateAsync(application, descriptor, cancellationToken);
        }
    }
}
