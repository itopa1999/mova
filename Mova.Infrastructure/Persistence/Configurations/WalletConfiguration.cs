using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("wallets");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserPublicId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.CompletedAt);

        builder.Property(x => x.ClosedAt);

        builder.ComplexProperty(
            x => x.TargetAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("target_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("target_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.AvailableAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("available_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("available_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.LockedAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("locked_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("locked_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.UnusedAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("unused_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("unused_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });


            builder.ComplexProperty(
                x => x.FundedAmount,
                money =>
                {
                    money.Property(x => x.MinorUnits)
                        .HasColumnName("funded_amount_minor_units")
                        .IsRequired();

                    money.Property(x => x.Currency)
                        .HasColumnName("funded_amount_currency")
                        .HasMaxLength(3)
                        .IsRequired();
                });

        builder.ComplexProperty(
            x => x.TotalReleasedAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("total_released_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("total_released_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.TotalWithdrawnAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("total_withdrawn_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("total_withdrawn_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.HasOne(x => x.Rule)
            .WithOne()
            .HasForeignKey<WalletRule>(x => x.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.ScheduledReleases)
            .WithOne()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Transactions)
            .WithOne()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.LedgerEntries)
            .WithOne()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.UserPublicId);

        builder.HasIndex(x => new
        {
            x.UserPublicId,
            x.Status
        });
    }
}