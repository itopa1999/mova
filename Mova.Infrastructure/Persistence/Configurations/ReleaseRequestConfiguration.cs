using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public class ReleaseRequestConfiguration : IEntityTypeConfiguration<ReleaseRequest>
{
    public void Configure(EntityTypeBuilder<ReleaseRequest> builder)
    {
        builder.ToTable("release_requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WalletId)
            .IsRequired();

        builder.Property(x => x.RequestedByUserPublicId)
            .IsRequired()
            .HasMaxLength(100);

        builder.ComplexProperty(
            x => x.Amount,
            money =>
            {
                money.Property(m => m.MinorUnits)
                    .HasColumnName("amount_minor_units")
                    .IsRequired();

                money.Property(m => m.Currency)
                    .HasColumnName("amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.Property(x => x.Destination)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.DestinationBankAccountId)
            .IsRequired(false);

        builder.Property(x => x.DestinationWalletId)
            .IsRequired(false);

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .HasDefaultValue(ReleaseRequestStatus.Pending)
            .IsRequired();

        builder.Property(x => x.UserNote)
            .HasMaxLength(500);

        builder.Property(x => x.RequestedAt)
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .IsRequired();

        builder.Property(x => x.DecidedAt)
            .IsRequired(false);

        builder.Property(x => x.DecidedByTrusteeId)
            .IsRequired(false);

        builder.Property(x => x.DecisionReason)
            .HasMaxLength(500);

        builder.Property(x => x.FailureReason)
            .HasMaxLength(500);

        // Email-link token — stored hashed, never in plain text
        builder.Property(x => x.ApprovalTokenHash)
            .HasColumnType("bytea")
            .IsRequired(false);

        builder.Property(x => x.TokenUsedAt)
            .IsRequired(false);

        builder.Property(x => x.TokenUsedFromIp)
            .HasMaxLength(45);   // fits IPv6

        // ── Relationships ─────────────────────────────────────────
        builder.HasOne(x => x.Wallet)
            .WithMany()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Restrict);   // never silently drop the audit trail

        builder.HasOne(x => x.DecidedByTrustee)
            .WithMany()
            .HasForeignKey(x => x.DecidedByTrusteeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ───────────────────────────────────────────────
        // List requests for a wallet (user-facing page, paginated)
        builder.HasIndex(x => new { x.WalletId, x.RequestedAt });

        // Expiry job: find pending requests whose window has passed
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });

        // Token lookup on the trustee review page
        builder.HasIndex(x => x.ApprovalTokenHash)
            .IsUnique()
            .HasFilter("\"ApprovalTokenHash\" IS NOT NULL");

        builder.HasIndex(x => x.WalletId)
            .IsUnique()
            .HasFilter("\"Status\" = 1")
            .HasDatabaseName("ux_release_requests_pending_per_wallet");

        builder.HasIndex(x => new { x.WalletId, x.Status, x.ExpiresAt });
    }
}