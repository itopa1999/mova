using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class WalletCategoryConfiguration
    : IEntityTypeConfiguration<WalletCategory>
{
    public void Configure(EntityTypeBuilder<WalletCategory> builder)
    {
        builder.ToTable("wallet_categories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Icon)
            .HasMaxLength(255);

        builder.Property(x => x.WalletType)
            .IsRequired();

        builder.HasIndex(x => new { x.WalletType, x.Name })
            .IsUnique()
            .HasDatabaseName("IX_wallet_categories_WalletType_Name");
    }
}