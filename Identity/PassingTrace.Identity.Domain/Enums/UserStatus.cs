namespace PassingTrace.Identity.Domain.Enums;

/// <summary>PassingTrace 账号的业务可用状态。</summary>
public enum UserStatus
{
    /// <summary>账号可正常登录、授权和刷新令牌。</summary>
    Active = 1,

    /// <summary>账号被停用；现有刷新令牌也不能再换取访问令牌。</summary>
    Disabled = 2
}
