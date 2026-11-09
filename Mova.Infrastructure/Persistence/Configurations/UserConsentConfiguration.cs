using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class UserConsentConfiguration : IEntityTypeConfiguration<UserConsent>
{
    public void Configure(EntityTypeBuilder<UserConsent> builder)
    {
        builder.ToTable("user_consents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserPublicId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.LegalDocumentId)
            .IsRequired();

        builder.Property(x => x.DocumentType)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.DocumentVersion)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.AcceptedAt)
            .IsRequired();

        // FK → LegalDocument. Restrict — never allow deleting a doc a user accepted.
        builder.HasOne(x => x.LegalDocument)
            .WithMany()
            .HasForeignKey(x => x.LegalDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.UserPublicId, x.LegalDocumentId })
            .IsUnique()
            .HasDatabaseName("ux_user_consents_user_document");

        builder.HasIndex(x => x.UserPublicId)
            .HasDatabaseName("ix_user_consents_user");

        builder.HasIndex(x => x.LegalDocumentId)
            .HasDatabaseName("ix_user_consents_document");
    }
}