using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("scheduled_releases")]
public class ScheduledRelease : BaseEntity
{
    public long WalletId { get; set; }

    public long WalletRuleId { get; set; }

    public Money Amount { get; set; }

    public DateTimeOffset ScheduledFor { get; set; }

    public ReleaseStatus Status { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }
}