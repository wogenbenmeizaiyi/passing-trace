using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Identity.Domain.Entities;

namespace PassingTrace.Identity.Infrastructure.Persistence.Configurations;

/// <summary>定义 User 聚合到 PostgreSQL 的表、列和唯一性约束。</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("identity_user", table =>
            table.HasComment("PassingTrace 本地身份用户。"));

        builder.Property(user => user.Id).HasColumnName("id");
        builder.Property(user => user.UserName)
            .HasColumnName("username")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(user => user.NormalizedUserName)
            .HasColumnName("normalized_username")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(512);
        builder.Property(user => user.SecurityStamp).HasColumnName("security_stamp");
        builder.Property(user => user.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        builder.Property(user => user.Email).HasColumnName("email");
        builder.Property(user => user.NormalizedEmail).HasColumnName("normalized_email");
        builder.Property(user => user.EmailConfirmed).HasColumnName("email_confirmed");
        builder.Property(user => user.PhoneNumber).HasColumnName("phone_number");
        builder.Property(user => user.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
        builder.Property(user => user.TwoFactorEnabled).HasColumnName("two_factor_enabled");
        builder.Property(user => user.LockoutEnd).HasColumnName("lockout_end");
        builder.Property(user => user.LockoutEnabled).HasColumnName("lockout_enabled");
        builder.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count");
        builder.Property(user => user.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(user => user.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
        builder.Property(user => user.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
        builder.Property(user => user.LastLoginAt).HasColumnName("last_login_at");

        // Identity 将用户名标准化，因此该唯一索引实现忽略大小写的唯一用户名。
        builder.HasIndex(user => user.NormalizedUserName)
            .IsUnique()
            .HasDatabaseName("uk_identity_user_normalized_username");
    }
}
