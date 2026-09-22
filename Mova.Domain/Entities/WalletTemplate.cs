using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("wallet_templates")]
public class WalletTemplate : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public long CategoryId { get; set; }

    public Money DefaultTargetAmount { get; set; } = Money.FromNaira(0);

    public Money DefaultReleaseAmount { get; set; } = Money.FromNaira(0);

    public ReleaseFrequency DefaultFrequency { get; set; }

    public string DefaultFrequencyConfig { get; set; } = string.Empty;

    public PayoutDestination DefaultPayoutDestination { get; set; } = PayoutDestination.Wallet;

    public string IconName { get; set; } = "Wallet";

    public string[] Tags { get; set; } = Array.Empty<string>();

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public int UsageCount { get; set; }

    public WalletCategory Category { get; set; } = null!;
}