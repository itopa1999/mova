using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("savings_circles")]
public class SavingsCircle : BaseEntity
{
    // Display name shown in lists, invites, and notifications (e.g. "Lagos Rent 2026")
    public string Name { get; set; } = string.Empty;

    // Optional short description ("What is this circle saving for?")
    public string? Description { get; set; }

    // Public ID of the organizer/moderator (created the circle)
    public string OrganizerUserPublicId { get; set; } = string.Empty;

    // Amount each member contributes per round
    public Money ContributionAmount { get; set; } = Money.FromNaira(0);

    // How often contributions are due (Daily / Weekly / Monthly)
    public ContributionFrequency Frequency { get; set; }

    // JSON when contributions are due — e.g. {"type":"weekly","daysOfWeek":[1,4]}
    public string FrequencyConfig { get; set; } = string.Empty;

    // Total member cap = total rounds (ROSCA: each member receives once)
    public int MemberCount { get; set; }

    // Security deposit held per member, refunded on clean completion
    public Money DepositAmount { get; set; } = Money.FromNaira(0);

    // Whether missing a contribution triggers a penalty
    public bool AllowPenalty { get; set; } = true;

    // Penalty charged per missed contribution
    public Money PenaltyAmount { get; set; } = Money.FromNaira(0);

    // Where the penalty is taken from: Deposit (deducted) or Round (added)
    public PenaltyAmountFrom PenaltyAmountFrom { get; set; } = PenaltyAmountFrom.Deposit;

    // Hours after due time before a miss flips to Defaulted
    public int GracePeriodHours { get; set; } = 24;

    // How payout order is decided (FixedOrder / RandomDraw / ModeratorSet)
    public RotationStrategy RotationStrategy { get; set; } = RotationStrategy.FixedOrder;

    // Earliest start date — actual start also depends on StartPolicy + accepted invites
    public DateTimeOffset StartDate { get; set; }

    // What happens on StartDate if not everyone has joined
    public StartPolicy StartPolicy { get; set; } = StartPolicy.WaitForAll;

    // When true, the circle only starts when the moderator explicitly triggers it
    // (still bounded by StartDate and by StartPolicy member-count rules).
    // When false, the circle auto-starts on StartDate per StartPolicy.
    public bool ModeratorStartsCircle { get; set; } = false;

    // Next contribution collection due (set when circle is Active)
    public DateTimeOffset? NextCollectionDate { get; set; }

    // Next payout due (set when circle is Active)
    public DateTimeOffset? NextPayoutDate { get; set; }

    // true = only invited users join; false = anyone with the code
    public bool InviteOnly { get; set; } = true;

    // Shareable join code (MOVA-XXXXXX), unique across circles
    [MaxLength(20)]
    public string? InviteCode { get; set; }

    // Draft → Active → Completed | Cancelled | Suspended
    public SavingsCircleStatus Status { get; set; } = SavingsCircleStatus.Draft;

    // Set when the circle reaches Completed
    public DateTimeOffset? CompletedAt { get; set; }

    // System pool bank account where contributions land before payout
    public long? PoolBankAccountId { get; set; }

    private readonly List<CircleMember> _members = new();

    // All members (including pending invites); see RotationPosition for order
    public IReadOnlyCollection<CircleMember> Members => _members.AsReadOnly();
}


[Table("circle_members")]
public class CircleMember : BaseEntity
{
    // FK to savings_circles.Id
    public long SavingsCircleId { get; set; }
    public SavingsCircle SavingsCircle { get; set; } = null!;

    // Raw identifier typed at invite time (email / phone / public ID)
    [MaxLength(320)]
    public string Identifier { get; set; } = string.Empty;

    // Resolved user's public ID — null while invite is pending (no MOVA account yet)
    public string? UserPublicId { get; set; }

    // Optional phone number captured at invite time
    public string? Phone { get; set; } = string.Empty;

    // Friendly name shown on invite list; replaced by real name on accept
    [MaxLength(120)]
    public string? DisplayName { get; set; } = string.Empty;

    // When the invite was issued
    public DateTimeOffset InvitedAt { get; set; } = DateTimeOffset.UtcNow;

    // When the invite was accepted (null while Invited)
    public DateTimeOffset? AcceptedAt { get; set; }

    // Member's circle wallet — created on accept, null while pending
    public long? WalletId { get; set; }
    public Wallet? Wallet { get; set; }

    // 1-based rotation slot; null for RandomDraw until draw runs
    public int? RotationPosition { get; set; }

    // Invited → Active → Defaulted / Exited / Completed
    public CircleMemberStatus Status { get; set; } = CircleMemberStatus.Invited;

    // Running total the member has paid in (across all rounds)
    public Money TotalContributed { get; set; } = Money.FromNaira(0);

    // Total the member has received as payout (0 until their round comes)
    public Money TotalReceived { get; set; } = Money.FromNaira(0);

    // Security deposit currently held for this member
    public Money DepositHeld { get; set; } = Money.FromNaira(0);

    // Number of times this member has missed a contribution
    public int MissedContributions { get; set; } = 0;

    // When the member received their payout (null until their round)
    public DateTimeOffset? PayoutReceivedAt { get; set; }
}


[Table("circle_rounds")]
public class CircleRound : BaseEntity
{
    // FK to savings_circles.Id
    public long SavingsCircleId { get; set; }
    public SavingsCircle SavingsCircle { get; set; } = null!;

    // 1-based round number (1..MemberCount); one round per member
    public int RoundNumber { get; set; }

    // When contributions for this round are due
    public DateTimeOffset CollectionDate { get; set; }

    // When the round's payout should be sent to the recipient
    public DateTimeOffset PayoutDate { get; set; }

    // Public ID of the member receiving this round's pot
    public string? RecipientUserPublicId { get; set; }

    // Total collected from all members for this round
    public Money PooledAmount { get; set; } = Money.FromNaira(0);

    // Total actually paid out to the recipient (after penalties/fees if any)
    public Money PaidOutAmount { get; set; } = Money.FromNaira(0);

    // Pending → Collecting → PaidOut | Failed | RequiresReview
    public CircleRoundStatus Status { get; set; } = CircleRoundStatus.Pending;

    // When the payout transaction completed
    public DateTimeOffset? PaidOutAt { get; set; }

    // External payment provider reference for the payout transfer
    public string? PayoutTransactionReference { get; set; }

    // True when the round needs manual review (e.g. member defaulted mid-round)
    public bool RequiresManualReview { get; set; }

    private readonly List<Contribution> _contributions = new();

    // Every member's contribution attempt for this round
    public IReadOnlyCollection<Contribution> Contributions => _contributions.AsReadOnly();
}


[Table("contributions")]
public class Contribution : BaseEntity
{
    // FK to circle_rounds.Id — which round this contribution belongs to
    public long CircleRoundId { get; set; }
    public CircleRound CircleRound { get; set; } = null!;

    // FK to circle_members.Id — the member who paid
    public long CircleMemberId { get; set; }
    public CircleMember CircleMember { get; set; } = null!;

    // Amount the member paid in (contribution + penalty if applicable)
    public Money Amount { get; set; } = Money.FromNaira(0);

    // Extra amount charged as penalty for a late/missed payment
    public Money PenaltyApplied { get; set; } = Money.FromNaira(0);

    // Pending → Paid | Missed | Defaulted | Refunded
    public ContributionStatus Status { get; set; } = ContributionStatus.Pending;

    // When the money actually arrived (null if not yet paid)
    public DateTimeOffset? CollectedAt { get; set; }

    // Provider transaction reference for the collection
    public string? PaymentReference { get; set; }

    // Reason the collection failed (for Missed/Defaulted states)
    public string? FailureReason { get; set; }
}