using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("wallet_rules")]
public class WalletRule : BaseEntity
{
    public long WalletId { get; set; }

    public Money Amount { get; set; }

    public ReleaseFrequency Frequency { get; set; }
    
    public string FrequencyConfig { get; set; } = string.Empty;

    public DateTimeOffset StartDate { get; set; }

    public DateTimeOffset? EndDate { get; set; }
}