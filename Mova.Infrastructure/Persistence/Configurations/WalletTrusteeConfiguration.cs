using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public class WalletTrusteeConfiguration : IEntityTypeConfiguration<WalletTrustee>
{
    public void Configure(EntityTypeBuilder<WalletTrustee> builder)
    {
        builder.ToTable("wallet_trustees");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublicId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(x => x.Name)
            .HasMaxLength(150);

        builder.Property(x => x.WalletId)
            .IsRequired();

        builder.HasOne(x => x.Wallet)
            .WithMany()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.PublicId)
            .IsUnique();

        builder.HasIndex(x => x.WalletId)
            .IsUnique();

        builder.HasIndex(x => x.Email);
    }
}