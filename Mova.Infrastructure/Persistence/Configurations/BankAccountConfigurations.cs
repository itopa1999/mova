using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mova.Domain.Entities;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Persistence.Configurations;

public class BankAccountConfiguration
    : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("bank_accounts");

        builder.HasKey(x => x.Id);

        // User relationship
        builder.Property(x => x.UserPublicId)
            .IsRequired();

        // Bank account details
        builder.Property(x => x.AccountNumber)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(x => x.AccountName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.BankCode)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(x => x.BankName)
            .IsRequired()
            .HasMaxLength(100);

        // Paystack recipient code
        builder.Property(x => x.PaystackRecipientCode)
            .HasMaxLength(100);

        // Status
        builder.Property(x => x.Status)
            .IsRequired()
            .HasDefaultValue(BankAccountStatus.Pending);

        builder.Property(x => x.IsDefault)
            .IsRequired()
            .HasDefaultValue(false);

        // Verification
        builder.Property(x => x.VerifiedAt);

        builder.Property(x => x.VerificationMessage)
            .HasMaxLength(500);

        // Metadata
        builder.Property(x => x.Institution)
            .HasMaxLength(100);

        builder.Property(x => x.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("NGN");

        // Indexes
        builder.HasIndex(x => x.UserPublicId)
            .HasDatabaseName("IX_bank_accounts_UserPublicId");

        builder.HasIndex(x => x.AccountNumber)
            .HasDatabaseName("IX_bank_accounts_AccountNumber");

        builder.HasIndex(x => x.PaystackRecipientCode)
            .HasDatabaseName("IX_bank_accounts_RecipientCode");

        builder.HasIndex(x => new { x.UserPublicId, x.IsDefault })
            .HasDatabaseName("IX_bank_accounts_UserPublicId_IsDefault")
            .IsUnique()
            .HasFilter("\"IsDefault\" = true");

        builder.HasIndex(x => new { x.UserPublicId, x.Status })
            .HasDatabaseName("IX_bank_accounts_UserPublicId_Status");

        builder.HasIndex(x => new { x.UserPublicId, x.AccountNumber })
            .HasDatabaseName("IX_bank_accounts_UserPublicId_AccountNumber")
            .IsUnique();
    }
}