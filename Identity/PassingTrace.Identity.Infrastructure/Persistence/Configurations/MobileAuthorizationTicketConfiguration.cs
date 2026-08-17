using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassingTrace.Identity.Domain.Entities;

namespace PassingTrace.Identity.Infrastructure.Persistence.Configurations;

public sealed class MobileAuthorizationTicketConfiguration
    : IEntityTypeConfiguration<MobileAuthorizationTicket>
{
    public void Configure(EntityTypeBuilder<MobileAuthorizationTicket> builder)
    {
        builder.ToTable("identity_mobile_authorization_ticket");
        builder.HasKey(ticket => ticket.Id);
        builder.Property(ticket => ticket.TicketHash).HasMaxLength(43).IsRequired();
        builder.Property(ticket => ticket.ClientId).HasMaxLength(100).IsRequired();
        builder.Property(ticket => ticket.RedirectUri).HasMaxLength(512).IsRequired();
        builder.Property(ticket => ticket.CodeChallenge).HasMaxLength(128).IsRequired();
        builder.Property(ticket => ticket.State).HasMaxLength(512);
        builder.Property(ticket => ticket.Nonce).HasMaxLength(512);
        builder.Property(ticket => ticket.NormalizedUsernameHash).HasMaxLength(43);
        builder.Property(ticket => ticket.RequestHash).HasMaxLength(43);
        builder.Property(ticket => ticket.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(ticket => ticket.TicketHash).IsUnique();
        builder.HasIndex(ticket => ticket.ExpiresAt);
        builder.HasOne(ticket => ticket.User)
            .WithMany()
            .HasForeignKey(ticket => ticket.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
