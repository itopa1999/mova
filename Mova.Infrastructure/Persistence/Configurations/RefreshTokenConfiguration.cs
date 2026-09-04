using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;


namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash)
               .IsRequired()
               .HasMaxLength(256);

        builder.HasIndex(x => x.TokenHash)
               .IsUnique();

        builder.HasIndex(x => x.UserPublicId);

        builder.HasIndex(x => x.ExpiresAt);
    }
}