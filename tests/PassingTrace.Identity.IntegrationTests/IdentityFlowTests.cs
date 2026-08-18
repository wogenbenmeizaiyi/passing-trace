using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace PassingTrace.Identity.IntegrationTests;

public sealed class IdentityFlowTests(IdentityWebApplicationFactory factory)
    : IClassFixture<IdentityWebApplicationFactory>
{
    [Fact]
    public async Task MobileRegistration_IssuesValidJwt_AndRefreshes()
    {
        using var client = CreateBrowserClient();
        var mobile = await RegisterMobileAsync(client, Unique("mobile"));
        Assert.False(string.IsNullOrWhiteSpace(mobile.Tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(mobile.Tokens.RefreshToken));
        Assert.False(string.IsNullOrWhiteSpace(mobile.DeviceSecret));

        var discovery = await GetJsonAsync(client, "/.well-known/openid-configuration");
        var issuer = discovery.RootElement.GetProperty("issuer").GetString()!;
        var jwksJson = await client.GetStringAsync(
            discovery.RootElement.GetProperty("jwks_uri").GetString()!);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            mobile.Tokens.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = new JsonWebKeySet(jwksJson).GetSigningKeys(),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = "passingtrace-api",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            });
        Assert.True(validation.IsValid, validation.Exception?.Message);
        Assert.Equal("passingtrace-mobile", validation.ClaimsIdentity.FindFirst("client_id")?.Value);
        Assert.Contains(
            "passingtrace.identity.login-approve",
            validation.ClaimsIdentity.FindFirst("scope")?.Value ?? string.Empty);

        var refreshed = await RefreshAsync(client, mobile.Tokens.RefreshToken!);
        Assert.NotEqual(mobile.Tokens.RefreshToken, refreshed.RefreshToken);
    }

    [Fact]
    public async Task WebAuthorization_RequiresMobileApproval_AndIssuesIndependentToken()
    {
        using var mobileBrowser = CreateBrowserClient();
        var mobile = await RegisterMobileAsync(mobileBrowser, Unique("qr"));

        using var webBrowser = CreateBrowserClient();
        var webGrantRequest = CreateAuthorizationRequest(
            "passingtrace-web",
            "http://localhost:5173/auth/callback");
        var first = await webBrowser.GetAsync(webGrantRequest.Uri);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var qrLocation = first.Headers.Location
            ?? throw new InvalidOperationException("缺少扫码页面地址。");
        Assert.StartsWith("/account/qr-login/", qrLocation.OriginalString);

        var qrPage = await webBrowser.GetAsync(qrLocation);
        qrPage.EnsureSuccessStatusCode();
        var qrHtml = await qrPage.Content.ReadAsStringAsync();
        Assert.Contains("<svg", qrHtml, StringComparison.OrdinalIgnoreCase);
        var antiforgery = ExtractAntiforgeryToken(qrHtml);
        var parsedQr = ParseQrLocation(qrLocation);

        using var detailsRequest = Authorized(
            HttpMethod.Get,
            $"/api/qr-login/transactions/{parsedQr.Code}",
            mobile.Tokens.AccessToken);
        (await mobileBrowser.SendAsync(detailsRequest)).EnsureSuccessStatusCode();

        using var approveRequest = Authorized(
            HttpMethod.Post,
            $"/api/qr-login/transactions/{parsedQr.Code}/approve",
            mobile.Tokens.AccessToken);
        approveRequest.Content = JsonContent.Create(new { });
        (await mobileBrowser.SendAsync(approveRequest)).EnsureSuccessStatusCode();

        var status = await webBrowser.GetFromJsonAsync<JsonElement>(
            $"/account/qr-login/{parsedQr.Id}/status");
        Assert.Equal("approved", status.GetProperty("status").GetString());

        var complete = await webBrowser.PostAsync(
            $"/account/qr-login/{parsedQr.Id}/complete",
            Form(("__RequestVerificationToken", antiforgery)));
        Assert.Equal(HttpStatusCode.Redirect, complete.StatusCode);
        var resumed = await webBrowser.GetAsync(complete.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, resumed.StatusCode);

        var callback = resumed.Headers.Location
            ?? throw new InvalidOperationException("缺少 Web 回调。");
        var code = QueryHelpers.ParseQuery(callback.Query)["code"].ToString();
        var webTokens = await ExchangeCodeAsync(
            webBrowser,
            code,
            webGrantRequest.Verifier,
            "passingtrace-web",
            "http://localhost:5173/auth/callback");
        Assert.NotEqual(mobile.Tokens.AccessToken, webTokens.AccessToken);
        Assert.Equal("passingtrace-web", new JsonWebToken(webTokens.AccessToken).GetClaim("client_id").Value);
    }

    [Fact]
    public async Task DirectBrowserRegistrationAndLogin_AreNotAvailable()
    {
        using var client = CreateBrowserClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/account/register")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/account/login")).StatusCode);
    }

    [Fact]
    public async Task MobileLaunch_RequiresRegisteredDeviceCredential()
    {
        using var client = CreateBrowserClient();
        var authorization = CreateAuthorizationRequest(
            "passingtrace-mobile",
            "com.passingtrace.mobile:/oauth2redirect",
            includeApprovalScope: true);
        var response = await client.PostAsJsonAsync(
            "/api/mobile/authorization-launches",
            new
            {
                clientId = "passingtrace-mobile",
                redirectUri = "com.passingtrace.mobile:/oauth2redirect",
                codeChallenge = authorization.Challenge,
                state = "invalid-device",
                deviceId = Guid.NewGuid(),
                deviceSecret = "not-a-device-secret"
            });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MobilePasswordLogin_BindsNewDevice_AndIssuesToken()
    {
        var username = Unique("mobile_login");
        const string password = "a secure passing trace phrase";
        using var registrationClient = CreateBrowserClient();
        await RegisterMobileAsync(registrationClient, username);

        using var loginClient = CreateBrowserClient();
        var authorization = CreateAuthorizationRequest(
            "passingtrace-mobile",
            "com.passingtrace.mobile:/oauth2redirect",
            includeApprovalScope: true);
        var response = await loginClient.PostAsJsonAsync(
            "/api/mobile/logins",
            new
            {
                username,
                password,
                clientId = "passingtrace-mobile",
                redirectUri = "com.passingtrace.mobile:/oauth2redirect",
                codeChallenge = authorization.Challenge,
                state = "mobile-password-login",
                deviceName = "Second Android"
            });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, body.GetProperty("deviceId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("deviceSecret").GetString()));

        var grantResponse = await loginClient.GetAsync(body.GetProperty("authorizeUrl").GetString()!);
        Assert.Equal(HttpStatusCode.Redirect, grantResponse.StatusCode);
        var code = QueryHelpers.ParseQuery(grantResponse.Headers.Location!.Query)["code"].ToString();
        var tokens = await ExchangeCodeAsync(
            loginClient,
            code,
            authorization.Verifier,
            "passingtrace-mobile",
            "com.passingtrace.mobile:/oauth2redirect");
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
    }

    [Fact]
    public async Task TokenEndpoint_RejectsWrongPkceVerifier()
    {
        using var client = CreateBrowserClient();
        var mobile = await RegisterMobileAsync(client, Unique("pkce"), exchange: false);
        var response = await client.PostAsync(
            "/connect/token",
            Form(
                ("grant_type", "authorization_code"),
                ("client_id", "passingtrace-mobile"),
                ("code", mobile.Code!),
                ("redirect_uri", "com.passingtrace.mobile:/oauth2redirect"),
                ("code_verifier", CreateVerifier())));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registration_EnforcesCaseInsensitiveUniqueUsername()
    {
        var username = Unique("Unique_User");
        using var first = CreateBrowserClient();
        await RegisterMobileAsync(first, username);
        using var duplicate = CreateBrowserClient();
        var response = await BeginAndCompleteRegistrationAsync(
            duplicate,
            username.ToLowerInvariant(),
            "a sufficiently long password");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentAutoLogin_BypassesQr_AndIssuesWebToken()
    {
        using var devFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DevelopmentAutoLogin:Enabled", "true");
            builder.UseSetting("DevelopmentAutoLogin:Username", "dev_auto");
            builder.UseSetting("DevelopmentAutoLogin:Password", "PassingTrace-Dev-2026!");
        });
        using var client = devFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

        var grantRequest = CreateAuthorizationRequest(
            "passingtrace-web",
            "http://localhost:5173/auth/callback");
        var response = await client.GetAsync(grantRequest.Uri);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location
            ?? throw new InvalidOperationException("缺少回调地址。");
        Assert.DoesNotContain("/account/qr-login/", location.OriginalString);

        var code = QueryHelpers.ParseQuery(location.Query)["code"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(code));
        var tokens = await ExchangeCodeAsync(
            client,
            code,
            grantRequest.Verifier,
            "passingtrace-web",
            "http://localhost:5173/auth/callback");
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.Equal(
            "passingtrace-web",
            new JsonWebToken(tokens.AccessToken).GetClaim("client_id").Value);

        // 自动登录写入了 Identity Cookie：再次发起同一授权请求仍直接出码，不再进扫码页。
        var again = await client.GetAsync(grantRequest.Uri);
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.DoesNotContain("/account/qr-login/", again.Headers.Location!.OriginalString);
        Assert.False(string.IsNullOrWhiteSpace(
            QueryHelpers.ParseQuery(again.Headers.Location.Query)["code"].ToString()));
    }

    [Fact]
    public async Task WebClient_LogoutAcceptsRegisteredCallbackAndReturnsState()    {
        using var client = CreateBrowserClient();
        await RegisterMobileAsync(client, Unique("logout"));
        var grant = await AuthorizeWithCookieAsync(
            client,
            "passingtrace-web",
            "http://localhost:5173/auth/callback");
        var tokens = await ExchangeCodeAsync(
            client,
            grant.Code,
            grant.Verifier,
            "passingtrace-web",
            "http://localhost:5173/auth/callback");

        const string callback = "http://localhost:5173/auth/logout-callback";
        var page = await client.GetAsync(
            "/connect/logout" +
            $"?id_token_hint={Uri.EscapeDataString(tokens.IdToken!)}" +
            $"&post_logout_redirect_uri={Uri.EscapeDataString(callback)}" +
            "&state=logout-state");
        page.EnsureSuccessStatusCode();
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/connect/logout",
            Form(
                ("__RequestVerificationToken", token),
                ("id_token_hint", tokens.IdToken!),
                ("post_logout_redirect_uri", callback),
                ("state", "logout-state")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("state=logout-state", response.Headers.Location!.OriginalString);
    }

    private HttpClient CreateBrowserClient() => factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<MobileRegistration> RegisterMobileAsync(
        HttpClient client,
        string username,
        bool exchange = true)
    {
        var authorization = CreateAuthorizationRequest(
            "passingtrace-mobile",
            "com.passingtrace.mobile:/oauth2redirect",
            includeApprovalScope: true);
        var registration = await BeginAndCompleteRegistrationAsync(
            client,
            username,
            "a secure passing trace phrase",
            authorization);
        registration.EnsureSuccessStatusCode();
        var body = await registration.Content.ReadFromJsonAsync<JsonElement>();
        var grantResponse = await client.GetAsync(body.GetProperty("authorizeUrl").GetString()!);
        Assert.Equal(HttpStatusCode.Redirect, grantResponse.StatusCode);
        var code = QueryHelpers.ParseQuery(grantResponse.Headers.Location!.Query)["code"].ToString();
        var tokens = exchange
            ? await ExchangeCodeAsync(
                client,
                code,
                authorization.Verifier,
                "passingtrace-mobile",
                "com.passingtrace.mobile:/oauth2redirect")
            : new TokenResponse(string.Empty, null, null);
        return new MobileRegistration(
            tokens,
            body.GetProperty("deviceId").GetGuid(),
            body.GetProperty("deviceSecret").GetString()!,
            exchange ? null : code);
    }

    private static async Task<HttpResponseMessage> BeginAndCompleteRegistrationAsync(
        HttpClient client,
        string username,
        string password,
        AuthorizationRequest? authorization = null)
    {
        authorization ??= CreateAuthorizationRequest(
            "passingtrace-mobile",
            "com.passingtrace.mobile:/oauth2redirect",
            includeApprovalScope: true);
        var intentResponse = await client.PostAsJsonAsync(
            "/api/mobile/registration-intents",
            new
            {
                username,
                clientId = "passingtrace-mobile",
                redirectUri = "com.passingtrace.mobile:/oauth2redirect",
                codeChallenge = authorization.Challenge,
                state = "mobile-registration"
            });
        intentResponse.EnsureSuccessStatusCode();
        var intent = await intentResponse.Content.ReadFromJsonAsync<JsonElement>();
        return await client.PostAsJsonAsync(
            "/api/mobile/registrations",
            new
            {
                intentId = intent.GetProperty("intentId").GetGuid(),
                username,
                password,
                bootstrapCode = "testing-bootstrap",
                deviceName = "Integration Android"
            });
    }

    private static async Task<AuthorizationGrant> AuthorizeWithCookieAsync(
        HttpClient client,
        string clientId,
        string redirectUri)
    {
        var request = CreateAuthorizationRequest(clientId, redirectUri);
        var response = await client.GetAsync(request.Uri);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return new AuthorizationGrant(
            QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString(),
            request.Verifier);
    }

    private static AuthorizationRequest CreateAuthorizationRequest(
        string clientId,
        string redirectUri,
        bool includeApprovalScope = false)
    {
        var verifier = CreateVerifier();
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var scope = "openid profile offline_access passingtrace.api" +
            (includeApprovalScope ? " passingtrace.identity.login-approve" : string.Empty);
        var uri = "/connect/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            "&code_challenge_method=S256" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            "&state=integration-test";
        return new AuthorizationRequest(uri, verifier, challenge);
    }

    private static async Task<TokenResponse> ExchangeCodeAsync(
        HttpClient client,
        string code,
        string verifier,
        string clientId,
        string redirectUri)
    {
        var response = await client.PostAsync(
            "/connect/token",
            Form(
                ("grant_type", "authorization_code"),
                ("client_id", clientId),
                ("code", code),
                ("redirect_uri", redirectUri),
                ("code_verifier", verifier)));
        response.EnsureSuccessStatusCode();
        return await ReadTokenResponseAsync(response);
    }

    private static async Task<TokenResponse> RefreshAsync(HttpClient client, string refreshToken)
    {
        var response = await client.PostAsync(
            "/connect/token",
            Form(
                ("grant_type", "refresh_token"),
                ("client_id", "passingtrace-mobile"),
                ("refresh_token", refreshToken)));
        response.EnsureSuccessStatusCode();
        return await ReadTokenResponseAsync(response);
    }

    private static async Task<TokenResponse> ReadTokenResponseAsync(HttpResponseMessage response)
    {
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new TokenResponse(
            document.GetProperty("access_token").GetString()!,
            document.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            document.TryGetProperty("id_token", out var id) ? id.GetString() : null);
    }

    private static ParsedQr ParseQrLocation(Uri location)
    {
        var absoluteLocation = location.IsAbsoluteUri
            ? location
            : new Uri(new Uri("https://identity.test"), location);
        var segments = absoluteLocation.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return new ParsedQr(
            Guid.Parse(segments[^1]),
            QueryHelpers.ParseQuery(absoluteLocation.Query)["code"].ToString());
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string uri) =>
        JsonDocument.Parse(await client.GetStringAsync(uri));

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "页面中没有防伪令牌。");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(value => new KeyValuePair<string, string>(value.Key, value.Value)));

    private static string CreateVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
    private static string Base64UrlEncode(byte[] value) => WebEncoders.Base64UrlEncode(value);
    private static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..32];

    private sealed record AuthorizationRequest(string Uri, string Verifier, string Challenge);
    private sealed record AuthorizationGrant(string Code, string Verifier);
    private sealed record TokenResponse(string AccessToken, string? RefreshToken, string? IdToken);
    private sealed record MobileRegistration(
        TokenResponse Tokens,
        Guid DeviceId,
        string DeviceSecret,
        string? Code);
    private sealed record ParsedQr(Guid Id, string Code);
}
