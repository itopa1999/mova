using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration
    : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WalletId);

        builder.Property(x => x.Type)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.Title)
            .IsRequired()
            .HasDefaultValue(string.Empty);


        builder.Property(x => x.Reference)
            .HasMaxLength(100);

        builder.Property(x => x.CompletedAt);

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

        builder.HasIndex(x => x.WalletId);

        builder.HasIndex(x => x.Reference)
            .IsUnique()
            .HasFilter("\"Reference\" IS NOT NULL");

        builder.HasIndex(x => x.Id)
            .HasDatabaseName("IX_transactions_ProcessingQueue")
            .HasFilter("\"IsDeleted\" = FALSE AND (\"Status\" = 1 OR \"Status\" = 2)");

        builder.HasIndex(x => new
            {
                x.UserPublicId,
                x.CreatedAt,
                x.Id
            })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_transactions_UserPublicId_CreatedAt_Id");

        builder.HasIndex(x => new
        {
            x.WalletId,
            x.Status
        });
    }
}