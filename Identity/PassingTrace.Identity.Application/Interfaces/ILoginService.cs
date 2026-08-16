using PassingTrace.Identity.Application.Models;

namespace PassingTrace.Identity.Application.Interfaces
{

    /// <summary>
    /// 登录接口
    /// </summary>
    public interface ILoginService
    {
        Task<LoginResult> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken);
    }
}
