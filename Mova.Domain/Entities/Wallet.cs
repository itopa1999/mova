using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;


[Table("wallets")]
public class Wallet : BaseEntity
{
    public string UserPublicId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Money TargetAmount { get; set; } = Money.FromNaira(0);
    // The total amount the user wants to allocate to this wallet.

    public Money TotalReleasedAmount { get; set; } = Money.FromNaira(0);
    // The cumulative amount that has been released from the locked balance.

    public Money AvailableAmount { get; set; } = Money.FromNaira(0);
    // The amount released during the current release window and currently available for withdrawal/use.
    // This amount is replaced by the next scheduled release if it was not withdrawn.

    public Money TotalWithdrawnAmount { get; set; } = Money.FromNaira(0);
    // The cumulative amount withdrawn by the user from released funds.

    public Money LockedAmount { get; set; } = Money.FromNaira(0);
    // The amount that remains locked and has not yet been released.

    public Money FundedAmount { get; set; } = Money.FromNaira(0);
    // The total amount funded into this wallet.

    public Money UnusedAmount { get; set; } = Money.FromNaira(0);
    // The cumulative amount from previous release windows that was not withdrawn before the next release.

    public WalletStatus Status { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public WalletRule? Rule { get; set; }

    private readonly List<ScheduledRelease> _scheduledReleases = new();

    public IReadOnlyCollection<ScheduledRelease> ScheduledReleases =>
        _scheduledReleases.AsReadOnly();

    private readonly List<Transaction> _transactions = new();

    public IReadOnlyCollection<Transaction> Transactions =>
        _transactions.AsReadOnly();

    private readonly List<LedgerEntry> _ledgerEntries = new();

    public IReadOnlyCollection<LedgerEntry> LedgerEntries =>
        _ledgerEntries.AsReadOnly();

}