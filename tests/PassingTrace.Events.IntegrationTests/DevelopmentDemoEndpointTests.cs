using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassingTrace.Events.Api.Development;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class DevelopmentDemoEndpointTests
{
    [Theory]
    [InlineData("Production", true, "dev", HttpStatusCode.NotFound)]
    [InlineData("Development", true, null, HttpStatusCode.Unauthorized)]
    [InlineData("Development", true, "another-user", HttpStatusCode.Forbidden)]
    [InlineData("Development", false, "dev", HttpStatusCode.NotFound)]
    public async Task Route_guards_reject_without_resolving_the_seeder(
        string environment, bool enabled, string? username, HttpStatusCode expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ApplicationName = typeof(DevelopmentDemoEndpointTests).Assembly.GetName().Name,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.Configure<DevelopmentDemoOptions>(options => options.Enabled = enabled);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
        builder.Services.AddAuthorization();
        // No seeder is registered: reaching it would fail instead of returning a guard status.
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapDevelopmentDemo();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
        if (username is not null) client.DefaultRequestHeaders.Add("X-Test-Username", username);

        using var response = await client.PostAsync("/api/v1/development/demo-data", null);

        Assert.Equal(expected, response.StatusCode);
        await app.StopAsync();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var username = Request.Headers["X-Test-Username"].ToString();
            if (string.IsNullOrEmpty(username)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", "1"), new Claim("preferred_username", username)], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
