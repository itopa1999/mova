using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class CircleRoundConfiguration : IEntityTypeConfiguration<CircleRound>
{
    public void Configure(EntityTypeBuilder<CircleRound> builder)
    {
        builder.ToTable("circle_rounds");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.RoundNumber)
            .IsRequired();

        builder.Property(r => r.CollectionDate)
            .IsRequired();

        builder.Property(r => r.PayoutDate)
            .IsRequired();

        builder.Property(r => r.RecipientUserPublicId)
            .HasMaxLength(100);

        builder.Property(r => r.PayoutTransactionReference)
            .HasMaxLength(200);

        builder.Property(r => r.RequiresManualReview)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.PaidOutAt);

        builder.ComplexProperty(
            x => x.PooledAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("pooled_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("pooled_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.ComplexProperty(
            x => x.PaidOutAmount,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("paid_out_amount_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("paid_out_amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });

        builder.HasOne(r => r.SavingsCircle)
            .WithMany()
            .HasForeignKey(r => r.SavingsCircleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Contributions)
            .WithOne(c => c.CircleRound)
            .HasForeignKey(c => c.CircleRoundId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.SavingsCircleId, r.RoundNumber })
            .IsUnique()
            .HasDatabaseName("ix_circle_rounds_circle_round_number");

        builder.HasIndex(r => new { r.Status, r.PayoutDate })
            .HasDatabaseName("ix_circle_rounds_status_payout_date");

        builder.HasIndex(r => r.RecipientUserPublicId)
            .HasDatabaseName("ix_circle_rounds_recipient");

        builder.HasIndex(r => r.RequiresManualReview)
            .HasFilter("\"RequiresManualReview\" = true")
            .HasDatabaseName("ix_circle_rounds_manual_review");
    }
}