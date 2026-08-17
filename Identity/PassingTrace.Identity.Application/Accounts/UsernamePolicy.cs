using System.Text.RegularExpressions;

namespace PassingTrace.Identity.Application.Accounts;

/// <summary>
/// 定义与持久化技术无关的用户名规则，供注册页面和 Identity 验证器共同使用。
/// </summary>
public static partial class UsernamePolicy
{
    /// <summary>用户名最小长度。</summary>
    public const int MinimumLength = 3;
    /// <summary>用户名最大长度。</summary>
    public const int MaximumLength = 32;

    /// <summary>ASP.NET Core Identity 允许的用户名字符集合。</summary>
    public const string AllowedCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";

    /// <summary>验证用户名是否符合 3–32 位 ASCII 字母、数字、短横线或下划线规则。</summary>
    public static bool IsValid(string? username) =>
        !string.IsNullOrWhiteSpace(username) && UsernameRegex().IsMatch(username);

    [GeneratedRegex("^[A-Za-z0-9_-]{3,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();
}
