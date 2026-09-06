using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("payouts")]
public class Payout : BaseEntity
{
    public string UserPublicId { get; private set; } = null!;
    public string WalletId { get; private set; } = null!;
    public string BankAccountId { get; private set; } = null!;

    public Money Amount { get; private set; }
    public Money Fee { get; private set; }
    public Money NetAmount { get; private set; }

    public string Reference { get; private set; } = null!;
    public string? Provider { get; private set; }
    public string? ProviderReference { get; private set; }


    public PayoutStatus Status { get; private set; }

    public DateTime? InitiatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }

    public string? FailureReason { get; private set; }
}