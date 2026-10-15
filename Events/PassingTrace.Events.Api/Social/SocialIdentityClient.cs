using System.Net.Http.Headers;
using PassingTrace.Core.Events;

namespace PassingTrace.Events.Api.Social;

public interface ISocialIdentityClient
{
    Task<PersonProfile?> ResolveAsync(string code, CancellationToken ct);
    Task<IReadOnlyDictionary<long, PersonProfile>> ProfilesAsync(IEnumerable<long> ids, CancellationToken ct);
    Task<IReadOnlyList<PersonProfile>> DevelopmentPeopleAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PersonProfile>>([]);
}

public sealed class SocialIdentityClient(HttpClient http, IHttpContextAccessor accessor) : ISocialIdentityClient
{
    public async Task<IReadOnlyList<PersonProfile>> DevelopmentPeopleAsync(CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "api/v1/development/people");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new DomainValidationException("本地社交演示账号未能准备，请检查开发自动登录配置。");
        return await response.Content.ReadFromJsonAsync<PersonProfile[]>(ct) ?? [];
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(
            accessor.HttpContext?.Request.Headers.Authorization.ToString() ?? "");
        return request;
    }

    public async Task<PersonProfile?> ResolveAsync(string code, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, "api/v1/people/resolve?code=" + Uri.EscapeDataString(code));
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new DomainValidationException("暂时无法查找好友，请检查好友码或稍后重试。");
        return await response.Content.ReadFromJsonAsync<PersonProfile>(ct);
    }

    public async Task<IReadOnlyDictionary<long, PersonProfile>> ProfilesAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var result = new Dictionary<long, PersonProfile>();
        foreach (var batch in ids.Distinct().Chunk(100))
        {
            using var request = Request(HttpMethod.Post, "api/v1/people/profiles");
            request.Content = JsonContent.Create(batch.Select(x => x.ToString()).ToArray());
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new DomainValidationException("好友资料暂时无法加载，请稍后重试。");
            foreach (var person in await response.Content.ReadFromJsonAsync<PersonProfile[]>(ct) ?? [])
                result[long.Parse(person.Id)] = person;
        }
        return result;
    }
}
