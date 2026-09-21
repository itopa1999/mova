using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Persistence.Configurations;

public class RenewalPolicyConfiguration : IEntityTypeConfiguration<RenewalPolicy>
{
    public void Configure(EntityTypeBuilder<RenewalPolicy> builder)
    {
        builder.ToTable("renewal_policies");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WalletId)
            .IsRequired();

        builder.Property(x => x.UserPublicId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.TriggerType)
            .HasConversion<int>()
            .IsRequired();

        builder.ComplexProperty(
            x => x.TriggerAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("trigger_amount_minor_units");

                money.Property(x => x.Currency)
                    .HasColumnName("trigger_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired(false);
            });

        builder.Property(x => x.RefillAmountType)
            .HasConversion<int>()
            .IsRequired();

        builder.ComplexProperty(
            x => x.RefillAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("refill_amount_minor_units");

                money.Property(x => x.Currency)
                    .HasColumnName("refill_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired(false);
            });

        builder.ComplexProperty(
            x => x.MinMainBalance,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("min_main_balance_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("min_main_balance_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.Property(x => x.MaxRenewals)
            .IsRequired(false);

        builder.Property(x => x.RenewalsCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.Status)
        .HasConversion<int>()
        .IsRequired()
        .HasDefaultValue(RenewalStatus.Active);

        builder.HasOne(x => x.Wallet)
            .WithOne()
            .HasForeignKey<RenewalPolicy>(x => x.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Events)
            .WithOne(x => x.RenewalPolicy)
            .HasForeignKey(x => x.RenewalPolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        // One active (non-deleted) policy per wallet.
        builder.HasIndex(x => x.WalletId)
            .IsUnique()
            .HasDatabaseName("ux_renewal_policies_wallet_id_active");

        builder.HasIndex(x => x.UserPublicId)
            .HasDatabaseName("ix_renewal_policies_user_public_id");
    }
}