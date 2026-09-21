using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("renewal_policies")]
public class RenewalPolicy : BaseEntity
{
    public long WalletId { get; set; }

    public string UserPublicId { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public RenewalStatus Status { get; set; } = RenewalStatus.Active;

    public RenewalTriggerType TriggerType { get; set; }

    public Money TriggerAmount { get; set; } = Money.FromNaira(0);
    public RefillAmountType RefillAmountType { get; set; }

    public Money RefillAmount { get; set; } = Money.FromNaira(0);

    public Money MinMainBalance { get; set; } = Money.FromNaira(0);

    public int? MaxRenewals { get; set; }

    public int RenewalsCount { get; set; }

    public Wallet Wallet { get; set; } = null!;

    private readonly List<RenewalEvent> _events = new();

    public IReadOnlyCollection<RenewalEvent> Events => _events.AsReadOnly();
}