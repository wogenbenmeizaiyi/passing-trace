using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Events.Api.DependencyInjection;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class CorsPolicyTests
{
    [Fact]
    public async Task DevelopmentWebOrigin_IsAllowedWithAuthorizationPreflight()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceApplication(configuration);

        await using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<ICorsPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(
            new DefaultHttpContext { RequestServices = provider },
            ApplicationExtensions.WebClientCorsPolicy);

        Assert.NotNull(policy);
        Assert.Contains("http://localhost:5173", policy.Origins);
        Assert.Contains("*", policy.Headers);
        Assert.Contains("*", policy.Methods);
    }

    [Fact]
    public async Task ProductionWithoutOrigins_DoesNotAllowCrossOriginRequests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraceApplication(new ConfigurationBuilder().Build());

        await using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<ICorsPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(
            new DefaultHttpContext { RequestServices = provider },
            ApplicationExtensions.WebClientCorsPolicy);

        Assert.NotNull(policy);
        Assert.Empty(policy.Origins);
    }
}
