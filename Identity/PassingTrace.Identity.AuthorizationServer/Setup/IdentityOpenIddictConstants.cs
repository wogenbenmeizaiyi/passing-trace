namespace PassingTrace.Identity.AuthorizationServer.Setup;

/// <summary>Identity 与业务 API 约定的稳定资源和 Scope 名称。</summary>
public static class IdentityOpenIddictConstants
{
    /// <summary>所有 PassingTrace 业务 API 共同使用的 JWT audience。</summary>
    public const string ApiResource = "passingtrace-api";

    /// <summary>允许调用 PassingTrace 业务 API 的 OAuth 2.0 Scope。</summary>
    public const string ApiScope = "passingtrace.api";

    /// <summary>仅移动客户端可申请的扫码批准 Scope。</summary>
    public const string LoginApprovalScope = "passingtrace.identity.login-approve";

    /// <summary>Identity 自身受保护移动接口的 audience。</summary>
    public const string IdentityResource = "passingtrace-identity";

    public const string MobileClientId = "passingtrace-mobile";
}
