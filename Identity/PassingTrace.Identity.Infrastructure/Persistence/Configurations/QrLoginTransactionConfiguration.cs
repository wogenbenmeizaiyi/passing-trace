using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Identity.Domain.Entities;

namespace PassingTrace.Identity.Infrastructure.Persistence.Configurations;

public sealed class QrLoginTransactionConfiguration
    : IEntityTypeConfiguration<QrLoginTransaction>
{
    public void Configure(EntityTypeBuilder<QrLoginTransaction> builder)
    {
        builder.ToTable("identity_qr_login_transaction");
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.CodeHash).HasMaxLength(43).IsRequired();
        builder.Property(transaction => transaction.BrowserBindingHash).HasMaxLength(43).IsRequired();
        builder.Property(transaction => transaction.ClientId).HasMaxLength(100).IsRequired();
        builder.Property(transaction => transaction.ProtectedAuthorizeRequest).HasMaxLength(4096).IsRequired();
        builder.Property(transaction => transaction.SourceIp).HasMaxLength(64).IsRequired();
        builder.Property(transaction => transaction.UserAgent).HasMaxLength(512).IsRequired();
        builder.Property(transaction => transaction.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(transaction => transaction.CodeHash).IsUnique();
        builder.HasIndex(transaction => new { transaction.Status, transaction.ExpiresAt });
        builder.HasOne(transaction => transaction.ApprovedUser)
            .WithMany()
            .HasForeignKey(transaction => transaction.ApprovedUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
