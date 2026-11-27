using System.Net;
using MediatR;
using Mova.Application.Interfaces.Admin;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Queries.Admin.UserQuery;

public sealed class GetUserDetails
{
    public sealed class Query : IRequest<BaseResult<UserDetailsDto>>
    {
        public string UserId { get; init; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<UserDetailsDto>>
    {
        private readonly IAdminIdentityService _adminIdentityService;

        public Handler(IAdminIdentityService adminIdentityService)
        {
            _adminIdentityService = adminIdentityService;
        }

        public async Task<BaseResult<UserDetailsDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var user = await _adminIdentityService.GetByPublicIdAsync(
                request.UserId,
                cancellationToken);

            if (user is null)
            {
                return new BaseResult<UserDetailsDto>(
                    HttpStatusCode.NotFound,
                    "User not found.",
                    null
                );
            }

            var data = new UserDetailsDto
            {
                Overview = new UserOverviewDto
                {
                    Id = user.Id,
                    PublicId = user.PublicId,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    FullName = user.FullName,
                    Email = user.Email,
                    ProfilePicture = user.ProfilePicture,
                    UserAccountStatus = user.AccountStatus,
                    Balance = new UserMoneyDto
                    {
                        Amount = user.Balance,
                        Currency = user.BalanceCurrency
                    },
                    Roles = user.Roles,
                    IsRestricted = user.RestrictionReason != null
                                   && user.RestrictionReason != "None",
                    RestrictionReason = user.RestrictionReason,
                    RestrictionReasonDetails = user.RestrictionReasonDetails
                },

                Profile = new UserProfileDto
                {
                    FirstName = user.FirstName,
                    OtherNames = user.OtherNames,
                    LastName = user.LastName,
                    Email = user.Email,
                    PhoneNumber = user.PhoneNumber,
                    ProfilePicture = user.ProfilePicture,
                    Roles = user.Roles,
                    PublicId = user.PublicId,
                    EmailConfirmed = user.EmailConfirmed,
                    PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                    BvnVerified = user.BvnVerified
                },

                Restriction = new UserRestrictionDto
                {
                    Reason = user.RestrictionReason ?? string.Empty,
                    Details = user.RestrictionReasonDetails,
                    RestrictedAt = user.RestrictedAt,
                    RestrictedBy = user.RestrictedBy,
                    ExpiresAt = user.RestrictionExpiresAt,
                    ReactivatedAt = user.ReactivatedAt
                },

                Audit = new UserAuditDto
                {
                    CreatedAt = user.CreatedAt,
                    CreatedBy = user.CreatedBy,
                    ModifiedAt = user.ModifiedAt,
                    ModifiedBy = user.ModifiedBy,
                    IsDeleted = user.IsDeleted,
                    DeletedAt = user.DeletedAt,
                    DeletedBy = user.DeletedBy
                },

                Security = new AdminUserSecurityDto
                {
                    TransactionPin = new UserTransactionPinDto
                    {
                        IsSet = user.TransactionPinIsSet,
                        SetAt = user.TransactionPinSetAt,
                        ChangedAt = user.TransactionPinChangedAt,
                        ResetAt = user.TransactionPinResetAt
                    },
                    LastKnownDeviceId = user.LastKnownDeviceId
                },

                Account = new UserAccountDto
                {
                    UserAccountStatus = user.AccountStatus,
                    OnboardingStage = user.OnboardingStage
                },

                Notifications = new AdminUserNotificationsDto
                {
                    NotifyLoginAlerts = user.NotifyLoginAlerts,
                    NotifyReleaseAlerts = user.NotifyReleaseAlerts,
                    NotifyProductUpdates = user.NotifyProductUpdates,
                    NotifyPromotions = user.NotifyPromotions
                }
            };

            return new BaseResult<UserDetailsDto>(
                HttpStatusCode.OK,
                "Users details retrieved successfully.",
                data
            );
        }
    }
}

public sealed record UserDetailsDto
{
    public UserOverviewDto Overview { get; init; } = new();
    public UserProfileDto Profile { get; init; } = new();
    public UserRestrictionDto Restriction { get; init; } = new();
    public UserAuditDto Audit { get; init; } = new();
    public AdminUserSecurityDto Security { get; init; } = new();
    public UserAccountDto Account { get; init; } = new();
    public AdminUserNotificationsDto Notifications { get; init; } = new();
}

public sealed record UserMoneyDto
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "NGN";
}

public sealed record UserOverviewDto
{
    public long Id { get; init; }
    public string PublicId { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? ProfilePicture { get; init; }
    public string UserAccountStatus { get; init; } = string.Empty;
    public UserMoneyDto Balance { get; init; } = new();
    public IReadOnlyList<UserRoleDto> Roles { get; init; } = [];
    public bool IsRestricted { get; init; }
    public string? RestrictionReason { get; init; }
    public string? RestrictionReasonDetails { get; init; }
}

public sealed record UserProfileDto
{
    public string FirstName { get; init; } = string.Empty;
    public string? OtherNames { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string? ProfilePicture { get; init; }
    public IReadOnlyList<UserRoleDto> Roles { get; init; } = [];
    public string PublicId { get; init; } = string.Empty;
    public bool EmailConfirmed { get; init; }
    public bool PhoneNumberConfirmed { get; init; }
    public bool BvnVerified { get; init; }
}

public sealed record UserRestrictionDto
{
    public string Reason { get; init; } = string.Empty;
    public string? Details { get; init; }
    public DateTimeOffset? RestrictedAt { get; init; }
    public string? RestrictedBy { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? ReactivatedAt { get; init; }
}

public sealed record UserAuditDto
{
    public DateTimeOffset? CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
    public string? ModifiedBy { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public string? DeletedBy { get; init; }
}

public sealed record UserAccountDto
{
    public string UserAccountStatus { get; init; } = string.Empty;
    public string OnboardingStage { get; init; } = string.Empty;
}

public sealed record UserTransactionPinDto
{
    public bool IsSet { get; init; }
    public DateTimeOffset? SetAt { get; init; }
    public DateTimeOffset? ChangedAt { get; init; }
    public DateTimeOffset? ResetAt { get; init; }
}

public sealed record AdminUserSecurityDto
{
    public UserTransactionPinDto TransactionPin { get; init; } = new();
    public string? LastKnownDeviceId { get; init; }
}

public sealed record AdminUserNotificationsDto
{
    public bool NotifyLoginAlerts { get; init; }
    public bool NotifyReleaseAlerts { get; init; }
    public bool NotifyProductUpdates { get; init; }
    public bool NotifyPromotions { get; init; }
}