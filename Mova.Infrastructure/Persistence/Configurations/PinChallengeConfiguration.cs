using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;


namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class PinChallengeConfiguration : IEntityTypeConfiguration<PinChallenge>
{
    public void Configure(EntityTypeBuilder<PinChallenge> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PinChallengeType)
               .IsRequired()
               .HasMaxLength(256);

        builder.HasIndex(c => new
        {
            c.UserPublicId,
            c.PinChallengeType,
            c.IsUsed,
            c.ExpiresAt
        });
    }
}