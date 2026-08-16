using PassingTrace.Identity.Domain.Enums;

namespace PassingTrace.Identity.Domain.Entities
{
    public sealed class User
    {
        public long Id { get; set; }

        /// <summary>
        /// 邮箱
        /// </summary>
        public string Email { get; set; } = null!;

        /// <summary>
        /// 登录密码。当前仅用于验证数据库链路，后续必须替换为安全的密码哈希。
        /// </summary>
        public string Password { get; set; } = null!;

        /// <summary>
        /// 邮箱是否已验证
        /// </summary>
        public bool EmailVerified { get; set; }

        /// <summary>
        /// 用户状态
        /// </summary>
        public UserStatus Status { get; set; } = UserStatus.Active;

        /// <summary>
        /// Token 版本，用于强制所有登录失效
        /// </summary>
        public int TokenVersion { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 最后登录时间
        /// </summary>
        public DateTime? LastLoginAt { get; set; }
    }

}
