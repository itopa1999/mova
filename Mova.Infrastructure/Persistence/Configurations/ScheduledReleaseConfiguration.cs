using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public class ScheduledReleaseConfiguration
    : IEntityTypeConfiguration<ScheduledRelease>
{
    public void Configure(EntityTypeBuilder<ScheduledRelease> builder)
    {
        builder.ToTable("scheduled_releases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.WalletId)
            .IsRequired();

        builder.Property(x => x.WalletRuleId)
            .IsRequired();

        builder.Property(x => x.ScheduledFor)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.FailedAttempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.ReleasedAt);

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

        builder.HasOne<WalletRule>()
            .WithMany()
            .HasForeignKey(x => x.WalletRuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.Status,
            x.ScheduledFor,
            x.Id
        })
            .HasDatabaseName("IX_scheduled_releases_Status_ScheduledFor_Id");

        builder.HasIndex(x => new
        {
            x.WalletRuleId,
            x.ScheduledFor,
            x.Status
        })
            .HasDatabaseName("IX_scheduled_releases_WalletRuleId_ScheduledFor_Status");

        builder.HasIndex(x => x.WalletId);
    }
}