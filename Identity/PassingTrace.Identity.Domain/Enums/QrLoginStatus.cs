namespace PassingTrace.Identity.Domain.Enums;

/// <summary>跨设备扫码登录事务状态。</summary>
public enum QrLoginStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Expired = 4,
    Consumed = 5
}
