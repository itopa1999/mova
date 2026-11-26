using Mova.Domain.Entities;

namespace Mova.Application.Extensions;

public static class UserExtensionHelpers
{
    public static UserPermissionsDto GetUserPermissions(this User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new UserPermissionsDto(
            user.PublicId,
            user.AccountStatus,
            user.GetStatusLabel(),
            user.GetStatusDescription(),
            user.CanLogin(),
            user.CanCreateWallets(),
            user.CanPerformSensitiveOperations(),
            user.CanReceivePayouts(),
            user.CanTopUp(),
            user.RestrictionReason?.ToString(),
            user.RestrictionReasonDetails,
            user.RestrictedAt,
            user.RestrictionExpiresAt);
    }
}