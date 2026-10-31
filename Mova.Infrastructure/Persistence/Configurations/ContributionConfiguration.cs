using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class ContributionConfiguration : IEntityTypeConfiguration<Contribution>
{
    public void Configure(EntityTypeBuilder<Contribution> builder)
    {
        builder.ToTable("contributions");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.PaymentReference)
            .HasMaxLength(200);

        builder.Property(c => c.FailureReason)
            .HasMaxLength(500);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.CollectedAt);

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

        builder.ComplexProperty(
            x => x.PenaltyApplied,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("penalty_applied_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("penalty_applied_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.HasOne(c => c.CircleRound)
            .WithMany(r => r.Contributions)
            .HasForeignKey(c => c.CircleRoundId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.CircleMember)
            .WithMany()
            .HasForeignKey(c => c.CircleMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.CircleRoundId, c.CircleMemberId })
            .IsUnique()
            .HasDatabaseName("ix_contributions_round_member");

        builder.HasIndex(c => c.PaymentReference)
            .IsUnique()
            .HasFilter("\"PaymentReference\" IS NOT NULL")
            .HasDatabaseName("ix_contributions_payment_reference");

        builder.HasIndex(c => c.Status)
            .HasDatabaseName("ix_contributions_status");

        builder.HasIndex(c => c.CircleMemberId)
            .HasDatabaseName("ix_contributions_member");
    }
}