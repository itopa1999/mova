namespace Mova.Application.Interfaces.Admin;

public class AdminUserListDto
{
    public long Id { get; set; }
    public string PublicId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OnboardingStage { get; set; } = string.Empty;
    public DateTimeOffset? DateJoined { get; set; }
}


public class UserDetailsDto
{
    public long Id { get; init; }
    public string PublicId { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;
    public string? OtherNames { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? ProfilePicture { get; init; }

    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }

    public bool EmailConfirmed { get; init; }
    public bool PhoneNumberConfirmed { get; init; }
    public bool BvnVerified { get; init; }

    public string AccountStatus { get; init; } = string.Empty;
    public string OnboardingStage { get; init; } = string.Empty;

    public decimal Balance { get; init; }
    public string BalanceCurrency { get; init; } = "NGN";

    public bool TransactionPinIsSet { get; init; }
    public DateTimeOffset? TransactionPinSetAt { get; init; }
    public DateTimeOffset? TransactionPinChangedAt { get; init; }
    public DateTimeOffset? TransactionPinResetAt { get; init; }

    public string? LastKnownDeviceId { get; init; }

    public bool NotifyLoginAlerts { get; init; }
    public bool NotifyReleaseAlerts { get; init; }
    public bool NotifyProductUpdates { get; init; }
    public bool NotifyPromotions { get; init; }

    public string? RestrictionReason { get; init; }
    public string? RestrictionReasonDetails { get; init; }
    public DateTimeOffset? RestrictedAt { get; init; }
    public string? RestrictedBy { get; init; }
    public DateTimeOffset? RestrictionExpiresAt { get; init; }
    public DateTimeOffset? ReactivatedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
    public string? ModifiedBy { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public string? DeletedBy { get; init; }

    public IReadOnlyList<UserRoleDto> Roles { get; set; } = [];
}

public sealed record UserRoleDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}