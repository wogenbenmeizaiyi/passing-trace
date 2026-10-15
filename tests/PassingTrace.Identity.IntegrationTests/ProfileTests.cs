using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PassingTrace.Identity.AuthorizationServer.Controllers;
using PassingTrace.Identity.AuthorizationServer.Profile;
using SkiaSharp;
using Xunit;

namespace PassingTrace.Identity.IntegrationTests;

public sealed class TestAvatarStorage : IAvatarStorage
{
    public ConcurrentDictionary<string, byte[]> Images { get; } = new();
    public bool FailWrites { get; set; }
    public Task PutAsync(string key, byte[] image, CancellationToken ct)
    {
        if (FailWrites) throw new IOException("Private implementation detail must not escape");
        Images[key] = image;
        return Task.CompletedTask;
    }
    public Task<byte[]> ReadAsync(string key, CancellationToken ct) => Task.FromResult(Images[key]);
    public Task DeleteAsync(string key, CancellationToken ct) { Images.TryRemove(key, out _); return Task.CompletedTask; }
}

public sealed partial class IdentityFlowTests
{
    private static MultipartFormDataContent ProfileForm(ProfileResponse profile, string name = "新的昵称", string bio = "收集日常", byte[]? image = null, bool remove = false)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(name), "nickname" },
            { new StringContent(bio), "bio" },
            { new StringContent(profile.Version.ToString()), "version" },
            { new StringContent(remove.ToString()), "removeAvatar" },
        };
        if (image is not null) form.Add(new ByteArrayContent(image), "avatar", "photo.png");
        return form;
    }

    private static byte[] TestImage()
    {
        using var surface = SKSurface.Create(new SKImageInfo(32, 24));
        surface.Canvas.Clear(SKColors.Green);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    [Fact]
    public async Task Profile_RequiresAuthentication()
    {
        using var client = CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/account/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/account/avatar")).StatusCode);
    }

    [Fact]
    public async Task Profile_UpdatesWithoutChangingLogin_AndRejectsStaleRevision()
    {
        using var client = CreateBrowserClient();
        var mobile = await RegisterMobileAsync(client, Unique("profile"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", mobile.Tokens.AccessToken);
        var original = (await client.GetFromJsonAsync<ProfileResponse>("/api/v1/account/profile"))!;
        Assert.Equal(original.Username, original.Nickname);
        Assert.False(original.HasAvatar);
        using var save = await client.PutAsync("/api/v1/account/profile", ProfileForm(original));
        save.EnsureSuccessStatusCode();
        var updated = (await save.Content.ReadFromJsonAsync<ProfileResponse>())!;
        Assert.Equal("新的昵称", updated.Nickname);
        Assert.Equal(original.Username, updated.Username);
        Assert.NotEqual(original.Version, updated.Version);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsync("/api/v1/account/profile", ProfileForm(original))).StatusCode);
        Assert.Equal(updated, await client.GetFromJsonAsync<ProfileResponse>("/api/v1/account/profile"));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("一二三四五六七八九十一二三四五六七八九十一二三四五", "")]
    [InlineData("a\nb", "")]
    public async Task Profile_ValidatesFields(string nickname, string bio)
    {
        using var client = CreateBrowserClient();
        var mobile = await RegisterMobileAsync(client, Unique("invalid"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", mobile.Tokens.AccessToken);
        var original = (await client.GetFromJsonAsync<ProfileResponse>("/api/v1/account/profile"))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/v1/account/profile", ProfileForm(original, nickname, bio))).StatusCode);
        Assert.Equal(original, await client.GetFromJsonAsync<ProfileResponse>("/api/v1/account/profile"));
    }

    [Fact]
    public async Task Avatar_IsPrivate_Normalized_AndRemovedOnlyAfterSave()
    {
        using var client = CreateBrowserClient();
        var mobile = await RegisterMobileAsync(client, Unique("avatar"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", mobile.Tokens.AccessToken);
        var original = (await client.GetFromJsonAsync<ProfileResponse>("/api/v1/account/profile"))!;
        using var save = await client.PutAsync("/api/v1/account/profile", ProfileForm(original, image: TestImage()));
        save.EnsureSuccessStatusCode();
        var profile = (await save.Content.ReadFromJsonAsync<ProfileResponse>())!;
        Assert.True(profile.HasAvatar);
        using var bytes = await client.GetAsync("/api/v1/account/avatar");
        Assert.Equal("image/png", bytes.Content.Headers.ContentType!.MediaType);
        using var decoded = SKBitmap.Decode(await bytes.Content.ReadAsByteArrayAsync());
        Assert.Equal(512, decoded.Width); Assert.Equal(512, decoded.Height);
        using var otherClient = CreateBrowserClient();
        var other = await RegisterMobileAsync(otherClient, Unique("other"));
        otherClient.DefaultRequestHeaders.Authorization = new("Bearer", other.Tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync("/api/v1/account/avatar")).StatusCode);
        Assert.False((await otherClient.GetFromJsonAsync<ProfileResponse>("/api/v1/account/profile"))!.HasAvatar);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/v1/account/profile", ProfileForm(profile, image: Encoding.UTF8.GetBytes("<svg onload='alert(1)'/>")))).StatusCode);
        Assert.True((await client.GetFromJsonAsync<ProfileResponse>("/api/v1/account/profile"))!.HasAvatar);
        using var remove = await client.PutAsync("/api/v1/account/profile", ProfileForm(profile, remove: true));
        remove.EnsureSuccessStatusCode();
        Assert.False((await remove.Content.ReadFromJsonAsync<ProfileResponse>())!.HasAvatar);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/account/avatar")).StatusCode);
    }

    [Fact]
    public async Task Avatar_StorageFailureDoesNotSaveTextOrExposeException()
    {
        using var client = CreateBrowserClient();
        var mobile = await RegisterMobileAsync(client, Unique("storage"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", mobile.Tokens.AccessToken);
        var original = (await client.GetFromJsonAsync<ProfileResponse>("/api/v1/account/profile"))!;
        var storage = (TestAvatarStorage)factory.Services.GetRequiredService<IAvatarStorage>();
        storage.FailWrites = true;
        try
        {
            using var response = await client.PutAsync("/api/v1/account/profile", ProfileForm(original, image: TestImage()));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain("Private implementation", await response.Content.ReadAsStringAsync());
            Assert.Equal(original, await client.GetFromJsonAsync<ProfileResponse>("/api/v1/account/profile"));
        }
        finally { storage.FailWrites = false; }
    }
}
