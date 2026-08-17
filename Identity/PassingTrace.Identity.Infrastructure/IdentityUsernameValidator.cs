using Microsoft.AspNetCore.Identity;
using PassingTrace.Identity.Application.Accounts;
using PassingTrace.Identity.Domain.Entities;

namespace PassingTrace.Identity.Infrastructure;

/// <summary>
/// 将应用层用户名规则接入 Identity，保证所有 UserManager 写入口都执行同一规则。
/// </summary>
internal sealed class IdentityUsernameValidator : IUserValidator<User>
{
    public Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (!UsernamePolicy.IsValid(user.UserName))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidUsername",
                Description = "用户名必须为 3–32 位字母、数字、下划线或短横线。"
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }
}
