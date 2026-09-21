using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public class RenewalEventConfiguration : IEntityTypeConfiguration<RenewalEvent>
{
    public void Configure(EntityTypeBuilder<RenewalEvent> builder)
    {
        builder.ToTable("renewal_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WalletId)
            .IsRequired();

        builder.Property(x => x.RenewalPolicyId)
            .IsRequired();

        builder.Property(x => x.UserPublicId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.Property(x => x.Result)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.ComplexProperty(
            x => x.Amount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.Property(x => x.TransactionId)
            .IsRequired(false);

        builder.HasOne(x => x.Wallet)
            .WithMany()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.RenewalPolicy)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.RenewalPolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Transaction)
            .WithMany()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.WalletId, x.OccurredAt })
            .HasDatabaseName("ix_renewal_events_wallet_id_occurred_at");

        builder.HasIndex(x => x.RenewalPolicyId)
            .HasDatabaseName("ix_renewal_events_renewal_policy_id");

        builder.HasIndex(x => x.TransactionId)
            .HasDatabaseName("ix_renewal_events_transaction_id");
    }
}