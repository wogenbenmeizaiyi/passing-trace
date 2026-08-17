namespace PassingTrace.Identity.AuthorizationServer.Setup;

/// <summary>读取声明式客户端配置，并集中判断移动/扫码登录模式。</summary>
public sealed class FirstPartyClientRegistry(IConfiguration configuration)
{
    private readonly IReadOnlyDictionary<string, OpenIddictClientRegistration> _clients =
        (configuration.GetSection("OpenIddict:Clients")
            .Get<OpenIddictClientRegistration[]>() ?? [])
        .ToDictionary(client => client.ClientId, StringComparer.Ordinal);

    public OpenIddictClientRegistration GetRequired(string clientId) =>
        _clients.TryGetValue(clientId, out var client)
            ? client
            : throw new InvalidOperationException($"未注册客户端 {clientId}。");

    public bool IsMobile(string clientId) =>
        string.Equals(GetRequired(clientId).LoginMode, "mobile", StringComparison.Ordinal);

    public bool IsRedirectUriAllowed(string clientId, string redirectUri) =>
        GetRequired(clientId).RedirectUris.Contains(redirectUri, StringComparer.Ordinal);
}
