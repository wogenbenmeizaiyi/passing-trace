using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PassingTrace.Identity.IntegrationTests;

public sealed partial class IdentityFlowTests
{
    [Fact]
    public async Task Public_people_require_authentication_and_do_not_expose_account_details()
    {
        using var client = CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/people/me")).StatusCode);
        var account = await RegisterMobileAsync(client, Unique("person"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", account.Tokens.AccessToken);
        var me = (await client.GetFromJsonAsync<JsonElement>("/api/v1/people/me"));
        var person = me.GetProperty("profile");
        var code = person.GetProperty("friendCode").GetString()!;
        Assert.Matches("^[0-9A-F]{16}$", code);
        Assert.StartsWith("data:image/png;base64,", me.GetProperty("qrDataUrl").GetString());
        using var response = await client.PostAsJsonAsync("/api/v1/people/profiles", new[] { person.GetProperty("id").GetString() });
        response.EnsureSuccessStatusCode();
        var profile = (await response.Content.ReadFromJsonAsync<JsonElement>())[0];
        Assert.Equal(new[] { "bio", "friendCode", "hasAvatar", "id", "nickname" }, profile.EnumerateObject().Select(x => x.Name).Order());
        var resolved = await client.GetFromJsonAsync<JsonElement>("/api/v1/people/resolve?code=" + code);
        Assert.Equal(person.GetProperty("id").GetString(), resolved.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/people/resolve?code=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/people/profiles", Enumerable.Repeat("1", 101).ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/development/people", null)).StatusCode);
    }
}
