using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;

namespace Mova.Domain.Entities;

[Table("bank_accounts")]
public class BankAccount : BaseEntity
{
    public string UserPublicId { get; set; }

    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;

    public string? PaystackRecipientCode { get; set; }

    public BankAccountStatus Status { get; set; }
    public bool IsDefault { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerificationMessage { get; set; }

    public bool ConsentGiven { get; set; }
    public DateTimeOffset? ConsentGivenAt { get; set; }
    public string? ConsentVersion { get; set; }

    public string? Institution { get; set; }
    public string? Currency { get; set; } = "NGN";
}