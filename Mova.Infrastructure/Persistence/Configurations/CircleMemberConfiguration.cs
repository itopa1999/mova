using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public class CircleMemberConfiguration : IEntityTypeConfiguration<CircleMember>
{
    public void Configure(EntityTypeBuilder<CircleMember> builder)
    {
        builder.ToTable("circle_members");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Identifier)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(x => x.UserPublicId)
            .HasMaxLength(100);

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.InvitedAt)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.MissedContributions)
            .IsRequired();

        builder.ComplexProperty(
            x => x.TotalContributed,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("total_contributed_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("total_contributed_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.TotalReceived,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("total_received_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("total_received_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.DepositHeld,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("deposit_held_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("deposit_held_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.HasOne(x => x.Wallet)
            .WithMany()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.WalletId);

        builder.HasIndex(x => x.UserPublicId);

        builder.HasIndex(x => new { x.SavingsCircleId, x.Identifier })
            .IsUnique();

        builder.HasIndex(x => new { x.SavingsCircleId, x.RotationPosition })
            .IsUnique()
            .HasFilter("\"RotationPosition\" IS NOT NULL");

        builder.HasIndex(x => new { x.SavingsCircleId, x.Status });
    }
}