using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("transactions")]
public class Transaction : BaseEntity
{
    public long? WalletId { get; set; }

    public Money Amount { get; set; }

    public TransactionType Type { get; set; }

    public TransactionStatus Status { get; set; }

    public string? Reference { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}