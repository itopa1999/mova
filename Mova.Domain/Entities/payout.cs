using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("payouts")]
public class Payout : BaseEntity
{
    public string UserPublicId { get; set; } = null!;
    public long WalletId { get; set; }
    public long BankAccountId { get; set; }

    public Money Amount { get; set; }
    public Money Fee { get; set; }
    public Money NetAmount { get; set; }

    public string Reference { get; set; } = null!;
    public string? Provider { get; set; }
    public string? ProviderReference { get; set; }


    public PayoutStatus Status { get; set; }

    public DateTimeOffset? InitiatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }

    public string? FailureReason { get; set; }

    public Wallet Wallet { get; set; } = null!;

    public BankAccount BankAccount { get; set; } = null!;
}