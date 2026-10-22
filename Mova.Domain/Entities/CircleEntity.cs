// using System.ComponentModel.DataAnnotations.Schema;
// using Mova.Domain.Common;
// using Mova.Domain.Enums;
// using Mova.Domain.ValueObjects;

// namespace Mova.Domain.Entities;

// [Table("savings_circles")]
// public class SavingsCircle : BaseEntity
// {
//     public string Name { get; set; } = string.Empty;
//     public string? Description { get; set; }

//     public string OrganizerUserPublicId { get; set; } = string.Empty;

//     // Contribution settings
//     public Money ContributionAmount { get; set; } = Money.FromNaira(0);
//     public ContributionFrequency Frequency { get; set; }
//     public int MemberCountROund { get; set; }              // == member count
//     public bool AllowPenalty { get; set; } = true;

//     // Risk controls
//     public Money PenaltyAmount { get; set; } = Money.FromNaira(0);   // per missed contribution
//     public PenaltyAmountFrom PenaltyAmountFrom { get; set; } = PenaltyAmountFrom.Deposit;
//     public int GracePeriodHours { get; set; } = 24;

//     // Rotation & scheduling
//     public RotationStrategy RotationStrategy { get; set; } = RotationStrategy.FixedOrder;
//     public DateTimeOffset StartDate { get; set; }
//     public DateTimeOffset? NextCollectionDate { get; set; }
//     public DateTimeOffset? NextPayoutDate { get; set; }

//     public SavingsCircleStatus Status { get; set; } = SavingsCircleStatus.Draft;
//     public DateTimeOffset? CompletedAt { get; set; }

//     // Pool account where contributions land before payout
//     public long? PoolBankAccountId { get; set; }

//     private readonly List<CircleMember> _members = new();
//     public IReadOnlyCollection<CircleMember> Members => _members.AsReadOnly();
// }


// [Table("circle_members")]
// public class CircleMember : BaseEntity
// {
//     public long SavingsCircleId { get; set; }
//     public SavingsCircle SavingsCircle { get; set; } = null!;

//     public string UserPublicId { get; set; } = string.Empty;
//     public long WalletId { get; set; }                 // the member's Circle-type wallet
//     public Wallet Wallet { get; set; } = null!;

//     public int RotationPosition { get; set; }          // 1-based; who receives in round N
//     public CircleMemberStatus Status { get; set; } = CircleMemberStatus.Invited;

//     public Money TotalContributed { get; set; } = Money.FromNaira(0);
//     public Money TotalReceived { get; set; } = Money.FromNaira(0);
//     public Money DepositHeld { get; set; } = Money.FromNaira(0);
//     public int MissedContributions { get; set; } = 0;

//     // Direct debit mandate (Paystack / Flutterwave auth code)
//     public string? MandateReference { get; set; }
//     public bool MandateActive { get; set; }

//     public DateTimeOffset? PayoutReceivedAt { get; set; }
// }


// [Table("circle_rounds")]
// public class CircleRound : BaseEntity
// {
//     public long SavingsCircleId { get; set; }
//     public SavingsCircle SavingsCircle { get; set; } = null!;

//     public int RoundNumber { get; set; }                   // 1..TotalRounds
//     public DateTimeOffset CollectionDate { get; set; }
//     public DateTimeOffset PayoutDate { get; set; }

//     public string? RecipientUserPublicId { get; set; }     // whose turn this round
//     public Money PooledAmount { get; set; } = Money.FromNaira(0);
//     public Money PaidOutAmount { get; set; } = Money.FromNaira(0);

//     public CircleRoundStatus Status { get; set; } = CircleRoundStatus.Pending;
//     public DateTimeOffset? PaidOutAt { get; set; }
//     public string? PayoutTransactionReference { get; set; }
//     public bool RequiresManualReview { get; set; }

//     private readonly List<Contribution> _contributions = new();
//     public IReadOnlyCollection<Contribution> Contributions => _contributions.AsReadOnly();
// }

// [Table("contributions")]
// public class Contribution : BaseEntity
// {
//     public long CircleRoundId { get; set; }                // FIX: was missing
//     public CircleRound CircleRound { get; set; } = null!;

//     public long CircleMemberId { get; set; }
//     public CircleMember CircleMember { get; set; } = null!;

//     public Money Amount { get; set; } = Money.FromNaira(0);
//     public Money PenaltyApplied { get; set; } = Money.FromNaira(0);

//     public ContributionStatus Status { get; set; } = ContributionStatus.Pending;
//     public DateTimeOffset? CollectedAt { get; set; }
//     public string? PaymentReference { get; set; }
//     public string? FailureReason { get; set; }
// }