using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;


[Table("wallets")]
public class Wallet : BaseEntity
{
    public string UserPublicId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Money TargetAmount { get; set; } = Money.FromNaira(0); // The total amount the user wants to save in this wallet

    public Money TotalReleasedAmount { get; set; } = Money.FromNaira(0); // The total amount that has been released from the wallet to the user

    public Money AvailableAmount { get; set; } = Money.FromNaira(0); // The amount that is currently available for use in the wallet

    public Money TotalWithdrawnAmount { get; set; } = Money.FromNaira(0); // The total amount that has been withdrawn from the wallet

    public Money LockedAmount { get; set; } = Money.FromNaira(0); // The amount that is currently locked and cannot be used until certain conditions are met
    public Money FundedAmount { get; set; } = Money.FromNaira(0); // The total amount that has been funded into the wallet, including both available and locked amounts

    public Money UnusedAmount { get; set; } // The amount that has been funded into the wallet but has not yet been allocated to any specific purpose or goal

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