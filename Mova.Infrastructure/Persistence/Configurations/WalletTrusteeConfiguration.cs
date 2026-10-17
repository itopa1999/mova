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

        builder.Property(x => x.PublicId).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Email).IsRequired().HasMaxLength(320);
        builder.Property(x => x.Name).HasMaxLength(150);

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .HasDefaultValue(WalletTrusteeStatus.Pending)
            .IsRequired();

        builder.Property(x => x.VerificationTokenHash).HasColumnType("bytea").IsRequired(false);
        builder.Property(x => x.TokenExpiresAt).IsRequired(false);
        builder.Property(x => x.InvitedAt).IsRequired();
        builder.Property(x => x.VerifiedAt).IsRequired(false);
        builder.Property(x => x.ConsentedFromIp).HasMaxLength(45);

        builder.HasIndex(x => x.PublicId).IsUnique();

        builder.HasIndex(x => x.Email);

        builder.HasIndex(x => x.VerificationTokenHash)
            .IsUnique()
            .HasFilter("\"VerificationTokenHash\" IS NOT NULL");

        builder.HasIndex(x => new { x.Status, x.TokenExpiresAt })
            .HasFilter("\"Status\" = 1");
    }
}