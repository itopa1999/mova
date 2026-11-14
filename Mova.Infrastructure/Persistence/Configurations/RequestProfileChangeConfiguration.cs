using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class RequestProfileChangeConfiguration
    : IEntityTypeConfiguration<RequestProfileChange>
{
    public void Configure(EntityTypeBuilder<RequestProfileChange> b)
    {
        b.ToTable("request_profile_changes");

        b.HasKey(x => x.Id);

        // ---- identity ----
        b.Property(x => x.UserPublicId)
            .IsRequired()
            .HasMaxLength(64);

        // ---- requested new values ----
        b.Property(x => x.NewFirstName).HasMaxLength(100);
        b.Property(x => x.NewOtherName).HasMaxLength(100);
        b.Property(x => x.NewLastName).HasMaxLength(100);
        b.Property(x => x.NewEmail).HasMaxLength(256);
        b.Property(x => x.NewPhoneNumber).HasMaxLength(32);

        // ---- reason / comment ----
        b.Property(x => x.RequestReason)
            .IsRequired()
            .HasMaxLength(500);

        b.Property(x => x.RequestComment).HasMaxLength(2000);

        // ---- snapshot of current values (filled in by the service) ----
        b.Property(x => x.OldFirstName).HasMaxLength(100);
        b.Property(x => x.OldOtherName).HasMaxLength(100);
        b.Property(x => x.OldLastName).HasMaxLength(100);
        b.Property(x => x.OldEmail).HasMaxLength(256);
        b.Property(x => x.OldPhoneNumber).HasMaxLength(32);

        b.Property(x => x.IsRejected)
            .IsRequired()
            .HasDefaultValue(false);

        b.Property(x => x.RejectionReason)
            .HasMaxLength(1000);

        b.Property(x => x.RejectedBy)
            .HasMaxLength(64);

        b.Property(x => x.RejectedAt);

        b.HasIndex(x => new { x.IsApproved, x.IsRejected });

        // ---- approval ----
        b.Property(x => x.IsApproved)
            .IsRequired()
            .HasDefaultValue(false);

        b.Property(x => x.ApprovedAt);

        // ---- indexes ----
        b.HasIndex(x => x.UserPublicId);
        b.HasIndex(x => new { x.UserPublicId, x.CreatedAt });
        b.HasIndex(x => x.IsApproved);

    }
}