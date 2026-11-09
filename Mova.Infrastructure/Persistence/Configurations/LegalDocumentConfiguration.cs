using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class LegalDocumentConfiguration : IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(EntityTypeBuilder<LegalDocument> builder)
    {
        builder.ToTable("legal_documents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.Version)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Content)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.PublishedAt)
            .IsRequired();

        builder.Property(x => x.IsCurrent)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(x => new { x.Type, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_legal_documents_type_version");

        builder.HasIndex(x => new { x.Type, x.IsCurrent })
            .HasDatabaseName("ix_legal_documents_type_is_current");

        builder.HasIndex(x => x.Type)
            .IsUnique()
            .HasFilter("\"IsCurrent\" = true")
            .HasDatabaseName("ux_legal_documents_one_current_per_type");
    }
}