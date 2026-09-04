using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("ledger_entries")]
public class LedgerEntry : BaseEntity
{
    public long? WalletId { get; set; }

    public long TransactionId { get; set; }

    public Money Amount { get; set; }

    public bool IsCredit { get; set; }
}