using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Identity.Domain.Entities;

namespace PassingTrace.Identity.Infrastructure.Persistence.Configurations;

public sealed class MobileDeviceConfiguration : IEntityTypeConfiguration<MobileDevice>
{
    public void Configure(EntityTypeBuilder<MobileDevice> builder)
    {
        builder.ToTable("identity_mobile_device");
        builder.HasKey(device => device.Id);
        builder.Property(device => device.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(device => device.SecretHash).HasMaxLength(43).IsRequired();
        builder.Property(device => device.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(device => device.SecretHash).IsUnique();
        builder.HasIndex(device => new { device.UserId, device.RevokedAt });
        builder.HasOne(device => device.User)
            .WithMany()
            .HasForeignKey(device => device.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
