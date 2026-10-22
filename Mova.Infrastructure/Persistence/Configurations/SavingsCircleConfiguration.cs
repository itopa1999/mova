using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public class SavingsCircleConfiguration : IEntityTypeConfiguration<SavingsCircle>
{
    public void Configure(EntityTypeBuilder<SavingsCircle> builder)
    {
        builder.ToTable("savings_circles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.OrganizerUserPublicId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Frequency)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.FrequencyConfig)
            .IsRequired();

        builder.Property(x => x.MemberCount)
            .IsRequired();

        builder.Property(x => x.AllowPenalty)
            .IsRequired();

        builder.Property(x => x.PenaltyAmountFrom)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.GracePeriodHours)
            .IsRequired();

        builder.Property(x => x.RotationStrategy)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.StartDate)
            .IsRequired();

        builder.Property(x => x.StartPolicy)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.InviteOnly)
            .IsRequired();

        builder.Property(x => x.InviteCode)
            .HasMaxLength(20);

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.ComplexProperty(
            x => x.ContributionAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("contribution_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("contribution_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.DepositAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("deposit_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("deposit_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.PenaltyAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("penalty_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("penalty_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.HasMany(x => x.Members)
            .WithOne(x => x.SavingsCircle)
            .HasForeignKey(x => x.SavingsCircleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.OrganizerUserPublicId);

        builder.HasIndex(x => x.Status);

        builder.HasIndex(x => x.InviteCode)
            .IsUnique()
            .HasFilter("\"InviteCode\" IS NOT NULL");

        builder.HasIndex(x => new { x.OrganizerUserPublicId, x.Status });

        builder.HasIndex(x => new { x.Status, x.StartDate });
    }
}