using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Persistence.Configurations;

public class VirtualAccountConfiguration : IEntityTypeConfiguration<VirtualAccount>
{
    public void Configure(EntityTypeBuilder<VirtualAccount> builder)
    {
        builder.ToTable("virtual_accounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserPublicId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.ProviderCustomerId)
            .HasMaxLength(150);

        builder.Property(x => x.ProviderAccountId)
            .HasMaxLength(150);

        builder.Property(x => x.AccountNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.BankName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.AccountName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Currency)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("NGN");

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue(VirtualAccountStatus.Active);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => new
        {
            x.Provider,
            x.AccountNumber
        })
        .IsUnique();

        builder.HasIndex(x => new
        {
            x.UserPublicId,
            x.Provider
        })
        .IsUnique();
    }
}