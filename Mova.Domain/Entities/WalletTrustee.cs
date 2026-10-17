using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Domain.Entities;

[Table("wallet_trustees")]
public class WalletTrustee : BaseEntity
{
    public string PublicId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public WalletTrusteeStatus Status { get; set; } = WalletTrusteeStatus.Pending;
    public byte[]? VerificationTokenHash { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }

    public DateTimeOffset InvitedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? ConsentedFromIp { get; set; }
}



[Table("release_requests")]
public class ReleaseRequest : BaseEntity
{
    public long WalletId { get; set; }
    public Wallet Wallet { get; set; } = null!;

    public string RequestedByUserPublicId { get; set; } = string.Empty;

    public Money Amount { get; set; } = Money.FromNaira(0);
    public PayoutDestination Destination { get; set; }
    public long? DestinationBankAccountId { get; set; }
    public long? DestinationWalletId { get; set; }

    public ReleaseRequestStatus Status { get; set; } = ReleaseRequestStatus.Pending;
    public string? UserNote { get; set; }

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(3);

    public DateTimeOffset? DecidedAt { get; set; }
    public long? DecidedByTrusteeId { get; set; }
    public WalletTrustee? DecidedByTrustee { get; set; }
    public string? DecisionReason { get; set; }
    public string? FailureReason { get; set; }

    public byte[]? ApprovalTokenHash { get; set; }
    public DateTimeOffset? TokenUsedAt { get; set; }
    public string? TokenUsedFromIp { get; set; }
}

public enum ReleaseRequestStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Expired = 4,
}

public enum WalletTrusteeStatus
{
    Pending  = 1,
    Verified = 2
}