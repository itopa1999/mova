using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("payouts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserPublicId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.ProviderReference)
            .HasMaxLength(150);

        builder.Property(x => x.FailureReason)
            .HasMaxLength(500);

        builder.Property(x => x.CompletedAt);

        builder.Property(x => x.FailedAt);

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

        builder.ComplexProperty(
            x => x.Fee,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("fee_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("fee_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.NetAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("net_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("net_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.HasIndex(x => x.UserPublicId);

        builder.HasIndex(x => x.ProviderReference)
            .IsUnique();

        builder.HasIndex(x => x.Reference)
            .IsUnique();

        builder.HasIndex(x => new
        {
            x.WalletId,
            x.Status
        });
    }
}