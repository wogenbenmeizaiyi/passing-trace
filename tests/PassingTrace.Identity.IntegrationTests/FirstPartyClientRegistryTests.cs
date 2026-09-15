using Microsoft.Extensions.Configuration;
using PassingTrace.Identity.AuthorizationServer.Setup;
using Xunit;

namespace PassingTrace.Identity.IntegrationTests;

public sealed class FirstPartyClientRegistryTests
{
    [Theory]
    [InlineData("http://localhost:5173/auth/callback", "http://localhost:5173/product")]
    [InlineData("https://web.example.test/auth/callback?code=private#fragment", "https://web.example.test/product")]
    [InlineData("com.passingtrace.desktop:/oauth2redirect", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("//untrusted.example.test/auth/callback", null)]
    [InlineData("https://name:password@web.example.test/auth/callback", null)]
    public void DownloadPage_UsesOnlyConfiguredWebOrigin(string redirectUri, string? expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenIddict:Clients:0:ClientId"] = "passingtrace-web",
            ["OpenIddict:Clients:0:RedirectUris:0"] = redirectUri
        }).Build();

        Assert.Equal(expected, new FirstPartyClientRegistry(configuration).GetWebProductUrl());
    }

    [Fact]
    public void DownloadPage_MissingWebRegistrationDoesNotBreakQrLogin()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Null(new FirstPartyClientRegistry(configuration).GetWebProductUrl());
    }
}
