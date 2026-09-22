using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Persistence.Configurations;

public class WalletTemplateConfiguration : IEntityTypeConfiguration<WalletTemplate>
{
    public void Configure(EntityTypeBuilder<WalletTemplate> builder)
    {
        builder.ToTable("wallet_templates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.CategoryId)
            .IsRequired();

        builder.ComplexProperty(
            x => x.DefaultTargetAmount,
            money =>
            {
                money.Property(m => m.MinorUnits)
                    .HasColumnName("default_target_amount_minor_units")
                    .IsRequired();

                money.Property(m => m.Currency)
                    .HasColumnName("default_target_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.DefaultReleaseAmount,
            money =>
            {
                money.Property(m => m.MinorUnits)
                    .HasColumnName("default_release_amount_minor_units")
                    .IsRequired();

                money.Property(m => m.Currency)
                    .HasColumnName("default_release_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.Property(x => x.DefaultFrequency)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.DefaultFrequencyConfig)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.DefaultPayoutDestination)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValue(PayoutDestination.Wallet);

        builder.Property(x => x.IconName)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("Wallet");

        // Tags stored as a Postgres text[] column
        builder.Property(x => x.Tags)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(x => x.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.UsageCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.IsActive)
            .HasDatabaseName("ix_wallet_templates_is_active");

        builder.HasIndex(x => x.SortOrder)
            .HasDatabaseName("ix_wallet_templates_sort_order");
    }
}