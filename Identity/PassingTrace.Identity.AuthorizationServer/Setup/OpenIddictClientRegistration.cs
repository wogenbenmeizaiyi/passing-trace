namespace PassingTrace.Identity.AuthorizationServer.Setup;

/// <summary>从配置文件读取的第一方公共客户端注册信息。</summary>
public sealed class OpenIddictClientRegistration
{
    /// <summary>OAuth 2.0 客户端唯一标识。</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>供管理界面和日志展示的客户端名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>`mobile` 允许移动启动票据登录；`qr` 只允许手机扫码批准。</summary>
    public string LoginMode { get; init; } = "qr";

    /// <summary>授权完成后允许返回的 URI 白名单。</summary>
    public string[] RedirectUris { get; init; } = [];

    /// <summary>退出完成后允许返回的 URI 白名单。</summary>
    public string[] PostLogoutRedirectUris { get; init; } = [];
}
