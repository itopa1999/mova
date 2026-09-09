using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class WalletCategoryConfiguration : IEntityTypeConfiguration<WalletCategory>
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
    }
}