using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace PassingTrace.Identity.IntegrationTests;

public sealed partial class IdentityFlowTests
{
    [Fact]
    public async Task WebPasswordLogin_CompletesPkceAndCannotReplayBrowserTransaction()
    {
        using var registration = CreateBrowserClient();
        var username = Unique("webpassword");
        await RegisterMobileAsync(registration, username);
        using var browser = CreateBrowserClient();
        var grant = CreateAuthorizationRequest("passingtrace-web", "http://localhost:5173/auth/callback");
        var start = await browser.GetAsync(grant.Uri);
        var location = start.Headers.Location!;
        var qr = ParseQrLocation(location);
        var page = await browser.GetStringAsync(location.OriginalString + "&mode=password");
        Assert.Contains("autocomplete=\"current-password\"", page);
        Assert.Contains("使用手机扫码登录", page);
        var antiforgery = ExtractAntiforgeryToken(page);
        var endpoint = $"/account/qr-login/{qr.Id}/password";
        var missingCsrf = await browser.PostAsync(endpoint, Form(("code", qr.Code),
            ("username", username), ("password", "a secure passing trace phrase")));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var complete = await browser.PostAsync(endpoint, Form(("__RequestVerificationToken", antiforgery),
            ("code", qr.Code), ("username", username), ("password", "a secure passing trace phrase")));
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        var resume = await browser.GetAsync(complete.Headers.Location);
        var code = QueryHelpers.ParseQuery(resume.Headers.Location!.Query)["code"].ToString();
        var tokens = await ExchangeCodeAsync(browser, code, grant.Verifier,
            "passingtrace-web", "http://localhost:5173/auth/callback");
        Assert.Equal("passingtrace-web", new JsonWebToken(tokens.AccessToken).GetClaim("client_id").Value);
        var replay = await browser.PostAsync(endpoint, Form(("__RequestVerificationToken", antiforgery),
            ("code", qr.Code), ("username", username), ("password", "a secure passing trace phrase")));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task WebPasswordLogin_RejectsWrongPasswordAndForeignBrowserWithoutEchoingPassword()
    {
        using var registration = CreateBrowserClient();
        var username = Unique("webwrong");
        await RegisterMobileAsync(registration, username);
        using var browser = CreateBrowserClient();
        var grant = CreateAuthorizationRequest("passingtrace-web", "http://localhost:5173/auth/callback");
        var start = await browser.GetAsync(grant.Uri);
        var location = start.Headers.Location!;
        var qr = ParseQrLocation(location);
        var page = await browser.GetStringAsync(location.OriginalString + "&mode=password");
        var wrong = await browser.PostAsync($"/account/qr-login/{qr.Id}/password", Form(
            ("__RequestVerificationToken", ExtractAntiforgeryToken(page)), ("code", qr.Code),
            ("username", username), ("password", "never-echo-this-password")));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        var html = WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync());
        Assert.Contains("账号或密码错误", html);
        Assert.DoesNotContain("never-echo-this-password", html);

        using var foreign = CreateBrowserClient();
        var foreignPage = await foreign.GetStringAsync(location.OriginalString + "&mode=password");
        var rejected = await foreign.PostAsync($"/account/qr-login/{qr.Id}/password", Form(
            ("__RequestVerificationToken", ExtractAntiforgeryToken(foreignPage)), ("code", qr.Code),
            ("username", username), ("password", "a secure passing trace phrase")));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }
}
