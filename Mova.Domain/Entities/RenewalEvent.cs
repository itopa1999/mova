using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("renewal_events")]
public class RenewalEvent : BaseEntity
{
    public long WalletId { get; set; }

    public long RenewalPolicyId { get; set; }

    public string UserPublicId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public RenewalResult Result { get; set; }

    public string? Reason { get; set; }

    public Money Amount { get; set; } = Money.FromNaira(0);

    public long? TransactionId { get; set; }

    public Wallet Wallet { get; set; } = null!;

    public RenewalPolicy RenewalPolicy { get; set; } = null!;

    public Transaction? Transaction { get; set; }
}