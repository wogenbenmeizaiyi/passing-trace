using PassingTrace.Identity.Application.Interfaces;
using PassingTrace.Identity.Application.Models;
using PassingTrace.Identity.Domain.Entities;
using PassingTrace.Identity.Domain.Enums;
using PassingTrace.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace PassingTrace.Identity.Application.Services
{
    internal sealed class LoginService(IdentityDbContext dbContext) : ILoginService
    {
        public async Task<LoginResult> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                return LoginResult.InvalidCredentials();
            }

            var user = await dbContext.Users
                .SingleOrDefaultAsync(
                    candidate => candidate.Email == email,
                    cancellationToken);

            var now = DateTime.UtcNow;

            if (user is null)
            {
                user = new User
                {
                    Email = email,
                    Password = password,
                    EmailVerified = false,
                    Status = UserStatus.Active,
                    TokenVersion = 0,
                    CreatedAt = now,
                    UpdatedAt = now,
                    LastLoginAt = now
                };

                dbContext.Users.Add(user);
            }
            else
            {
                if (user.Status != UserStatus.Active)
                {
                    return LoginResult.Inactive();
                }

                if (user.Password != password)
                {
                    return LoginResult.InvalidCredentials();
                }

                user.LastLoginAt = now;
                user.UpdatedAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            var accessToken = new AccessToken(
                $"dev-{Guid.NewGuid():N}",
                DateTimeOffset.UtcNow.AddHours(1));

            return LoginResult.Succeeded(accessToken);
        }
    }
}
