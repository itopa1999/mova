using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public class LedgerEntryConfiguration
    : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WalletId);

        builder.Property(x => x.TransactionId)
            .IsRequired();

        builder.Property(x => x.IsCredit)
            .IsRequired();

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

        builder.HasOne<Wallet>()
            .WithMany()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(x => x.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.WalletId);

        builder.HasIndex(x => x.TransactionId);

        builder.HasIndex(x => new
        {
            x.WalletId,
            x.CreatedAt
        });
    }
}