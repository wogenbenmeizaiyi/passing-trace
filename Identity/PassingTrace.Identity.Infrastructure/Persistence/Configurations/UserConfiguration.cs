using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Identity.Domain.Entities;

namespace PassingTrace.Identity.Infrastructure.Persistence.Configurations
{
    public sealed class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("user", table => table.HasComment("系统用户，维护登录信息、展示信息与账号状态。"));

            builder.HasKey(user => user.Id);

            builder.Property(user => user.Id)
                .HasColumnName("id")
                .HasComment("用户唯一标识")
                .ValueGeneratedOnAdd();

            builder.Property(user => user.Email)
                .HasColumnName("email")
                .HasMaxLength(256)
                .IsRequired()
                .HasComment("登录邮箱");

            builder.Property(user => user.Password)
                .HasColumnName("password")
                .HasMaxLength(256)
                .IsRequired()
                .HasComment("登录密码（仅限当前开发阶段，后续改为密码哈希）");

            builder.Property(user => user.EmailVerified)
                .HasColumnName("email_verified")
                .IsRequired()
                .HasComment("邮箱是否已验证");

            builder.Property(user => user.Status)
                .HasColumnName("status")
                .HasComment("用户状态")
                .HasConversion<int>();

            builder.Property(user => user.TokenVersion)
                .HasColumnName("token_version")
                .IsRequired()
                .HasComment("Token 版本，用于强制所有登录失效");

            builder.Property(user => user.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired()
                .HasComment("创建时间");

            builder.Property(user => user.UpdatedAt)
                .HasColumnName("updated_at")
                .IsRequired()
                .HasComment("更新时间");

            builder.Property(user => user.LastLoginAt)
                .HasColumnName("last_login_at")
                .HasComment("最后登录时间");

            builder.HasIndex(user => user.Email)
                .IsUnique()
                .HasDatabaseName("uk_user_email");

        }
    }
}
