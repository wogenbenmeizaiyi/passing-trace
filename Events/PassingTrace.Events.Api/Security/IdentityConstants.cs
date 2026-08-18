namespace PassingTrace.Events.Api.Security;

/// <summary>
/// 业务 API 与 Identity 之间的协议约定，仅通过 OAuth/OIDC 协议建立信任，
/// 不引用 Identity 的任何程序集。
/// </summary>
public static class IdentityConstants
{
    /// <summary>Access Token 的 audience，与 Identity 签发的 API Resource 一致。</summary>
    public const string ApiAudience = "passingtrace-api";

    /// <summary>访问业务 API 所需的 Scope，与 Identity 签发的 Scope 名称一致。</summary>
    public const string ApiScope = "passingtrace.api";
}
