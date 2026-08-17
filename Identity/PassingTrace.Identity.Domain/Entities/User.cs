using Microsoft.AspNetCore.Identity;
using PassingTrace.Identity.Domain.Enums;

namespace PassingTrace.Identity.Domain.Entities;

/// <summary>
/// PassingTrace 的本地用户。用户名和密码能力由 ASP.NET Core Identity 提供。
/// </summary>
public sealed class User : IdentityUser<long>
{
    /// <summary>账号的业务状态；Disabled 用户即使凭据正确也不能继续授权。</summary>
    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>账号创建时间，统一使用 UTC 时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>账号业务数据最后更新时间，统一使用 UTC 时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>最近一次用户名密码登录成功的时间。</summary>
    public DateTimeOffset? LastLoginAt { get; set; }
}
