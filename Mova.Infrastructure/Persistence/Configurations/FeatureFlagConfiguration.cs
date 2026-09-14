using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class FeatureFlagConfiguration
    : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flags");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Metadata)
            .HasColumnType("jsonb");

        builder.Property(x => x.IsEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(x => x.Name)
            .IsUnique();
    }
}