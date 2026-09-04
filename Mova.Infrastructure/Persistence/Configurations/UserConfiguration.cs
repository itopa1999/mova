using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Infrastructure.Identity;

namespace Mova.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FirstName)
               .IsRequired()
               .HasMaxLength(100);

        builder.Property(x => x.LastName)
               .IsRequired()
               .HasMaxLength(100);

        builder.Property(x => x.OtherNames)
               .HasMaxLength(100);

        builder.HasIndex(x => x.Email)
               .IsUnique();

        builder.HasIndex(x => x.PhoneNumber)
               .IsUnique();

        builder.HasIndex(x => x.PublicId)
            .IsUnique();

       builder.ComplexProperty(
            x => x.Balance,
            money =>
            {
                money.Property(x => x.MinorUnits)
                    .HasColumnName("balance_minor_units")
                    .IsRequired();

                money.Property(x => x.Currency)
                    .HasColumnName("balance_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });
    }
}