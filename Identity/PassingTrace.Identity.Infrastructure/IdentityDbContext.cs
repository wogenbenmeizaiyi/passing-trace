using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PassingTrace.Identity.Domain.Entities;

namespace PassingTrace.Identity.Infrastructure;

/// <summary>
/// Identity 与 OpenIddict 共用的 EF Core 工作单元；泛型主键统一为 long。
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<User, IdentityRole<long>, long>(options)
{
    public DbSet<MobileAuthorizationTicket> MobileAuthorizationTickets =>
        Set<MobileAuthorizationTicket>();

    public DbSet<MobileDevice> MobileDevices => Set<MobileDevice>();

    public DbSet<QrLoginTransaction> QrLoginTransactions =>
        Set<QrLoginTransaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // 必须先让 Identity/OpenIddict 建立默认模型，再覆盖表名和字段配置。
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        // V1 暂不使用角色，但保留完整 Identity 表结构，方便以后增加角色授权。
        builder.Entity<IdentityRole<long>>().ToTable("identity_role");
        builder.Entity<IdentityRoleClaim<long>>().ToTable("identity_role_claim");
        builder.Entity<IdentityUserClaim<long>>().ToTable("identity_user_claim");
        builder.Entity<IdentityUserLogin<long>>().ToTable("identity_user_login");
        builder.Entity<IdentityUserRole<long>>().ToTable("identity_user_role");
        builder.Entity<IdentityUserToken<long>>().ToTable("identity_user_token");
    }
}
