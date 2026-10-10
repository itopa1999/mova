using Mova.Domain.Enums;

namespace Mova.Infrastructure.Identity.Extensions;

/// <summary>
/// Account status transitions and feature-gating helpers for <see cref="User"/>.
///
/// Each public "action" method corresponds to one row of the account-lifecycle
/// policy. Status + restriction reason are stored on the user; feature gates
/// read them back to decide what the user can still do.
///
/// Restrictions set an optional <c>RestrictionExpiresAt</c>. When the expiry
/// passes, <see cref="TryAutoReactivate"/> can be called (from a background
/// job) to lift the restriction in place.
///
/// Audit fields (<c>RestrictedAt</c>, <c>RestrictedBy</c>,
/// <c>RestrictionReason</c>, <c>RestrictionReasonDetails</c>) are preserved
/// across reactivation so you always have a trail of what happened and who
/// did it.
/// </summary>
public static class UserAccountExtensions
{
    // ═════════════════════════════════════════════════════════
    // 1. Status checks
    // ═════════════════════════════════════════════════════════

    public static bool HasActiveStatus(this User user) =>
        user.AccountStatus == UserAccountStatus.Active;

    public static bool HasRestrictedStatus(this User user) =>
        user.AccountStatus == UserAccountStatus.Restricted;

    public static bool HasSuspendedStatus(this User user) =>
        user.AccountStatus == UserAccountStatus.Suspended;

    public static bool IsDeactivated(this User user) =>
        user.AccountStatus == UserAccountStatus.Deactivated;

    public static bool IsClosed(this User user) =>
        user.AccountStatus == UserAccountStatus.Closed;

    /// <summary>
    /// True when the account is Active and, if it carries an expiry,
    /// that expiry has not yet passed.
    /// </summary>
    public static bool IsEffectivelyActive(this User user)
    {
        if (user.AccountStatus != UserAccountStatus.Active) return false;
        if (user.RestrictionExpiresAt is null) return true;
        return user.RestrictionExpiresAt.Value > DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// True when the account is Restricted/Suspended and its expiry has passed.
    /// Useful for auto-reactivation sweeps.
    /// </summary>
    public static bool HasExpiredRestriction(this User user)
    {
        if (user.AccountStatus is not (
            UserAccountStatus.Restricted or
            UserAccountStatus.Suspended))
        {
            return false;
        }

        return user.RestrictionExpiresAt.HasValue &&
               user.RestrictionExpiresAt.Value <= DateTimeOffset.UtcNow;
    }

    // ═════════════════════════════════════════════════════════
    // 2. Feature gates
    // ═════════════════════════════════════════════════════════

    /// <summary>
    /// Can the user log in at all? Blocked for Suspended/Closed/Deactivated.
    /// Restricted users CAN log in but with limited features.
    /// </summary>
    public static bool CanLogin(this User user) =>
        user.AccountStatus is UserAccountStatus.Active
            or UserAccountStatus.Restricted;

    /// <summary>
    /// Can the user create new wallets? Blocked for anything but Active.
    /// </summary>
    public static bool CanCreateWallets(this User user) =>
        user.AccountStatus == UserAccountStatus.Active;

    /// <summary>
    /// Can the user perform sensitive operations (withdrawals, transfers,
    /// PIN change)? Blocked for anything but Active.
    /// </summary>
    public static bool CanPerformSensitiveOperations(this User user) =>
        user.AccountStatus == UserAccountStatus.Active;

    /// <summary>
    /// Can releases/payouts land on the user's linked bank account?
    /// Blocked for Suspended and Closed — funds stay put.
    /// </summary>
    public static bool CanReceivePayouts(this User user) =>
        user.AccountStatus is UserAccountStatus.Active
            or UserAccountStatus.Restricted;

    /// <summary>
    /// Can the user top up their balance?
    /// Blocked only for Closed accounts.
    /// </summary>
    public static bool CanTopUp(this User user) =>
        user.AccountStatus != UserAccountStatus.Closed;

    /// <summary>
    /// Generic gate — returns true if the user is allowed to perform the
    /// given action. Central policy so callers don't reimplement the rules.
    /// </summary>
    public static bool CanPerform(this User user, UserAction action) =>
        action switch
        {
            UserAction.Login => user.CanLogin(),
            UserAction.WalletCreation => user.CanCreateWallets(),
            UserAction.SensitiveOperation => user.CanPerformSensitiveOperations(),
            UserAction.ReceivePayout => user.CanReceivePayouts(),
            UserAction.BalanceTopUp => user.CanTopUp(),
            _ => false,
        };

    // ═════════════════════════════════════════════════════════
    // 3. Scenario actions — one per policy row
    // ═════════════════════════════════════════════════════════

    /// <summary>
    /// Minor violation — the user is warned but the account stays usable.
    /// We record the reason for audit but do NOT change AccountStatus.
    /// Call <see cref="RestrictForRepeatedMisuse"/> if the warning is escalated.
    /// </summary>
    public static void WarnForMinorViolation(
        this User user,
        AccountRestrictionReason reason,
        string details,
        string performedBy,
        TimeSpan? expiresIn = null)
    {
        user.AccountStatus = UserAccountStatus.Active;

        user.RestrictionReason = reason;
        user.RestrictionReasonDetails = details;
        user.RestrictedAt = DateTimeOffset.UtcNow;
        user.RestrictedBy = performedBy;
        user.RestrictionExpiresAt = expiresIn.HasValue
            ? DateTimeOffset.UtcNow.Add(expiresIn.Value)
            : null;

        user.ModifiedAt = DateTimeOffset.UtcNow;
        user.ModifiedBy = performedBy;
    }

    /// <summary>
    /// Repeated misuse — restrict selected actions (wallet creation, sensitive
    /// operations). The user can still log in and use existing wallets.
    /// Existing schedules continue.
    /// </summary>
    public static void RestrictForRepeatedMisuse(
        this User user,
        AccountRestrictionReason reason,
        string details,
        string performedBy,
        TimeSpan? duration = null)
    {
        user.AccountStatus = UserAccountStatus.Restricted;
        user.RestrictionReason = reason;
        user.RestrictionReasonDetails = details;
        user.RestrictedAt = DateTimeOffset.UtcNow;
        user.RestrictedBy = performedBy;
        user.RestrictionExpiresAt = duration.HasValue
            ? DateTimeOffset.UtcNow.Add(duration.Value)
            : null;   // null = indefinite, requires manual reactivation

        user.ModifiedAt = DateTimeOffset.UtcNow;
        user.ModifiedBy = performedBy;
    }

    /// <summary>
    /// Suspected account takeover — block sensitive operations and pause
    /// payouts until the user verifies. Uses SecurityReview as the reason.
    /// </summary>
    public static void FlagSuspectedTakeover(
        this User user,
        string details,
        string performedBy,
        TimeSpan? duration = null)
    {
        user.AccountStatus = UserAccountStatus.Restricted;
        user.RestrictionReason = AccountRestrictionReason.SecurityReview;
        user.RestrictionReasonDetails = details;
        user.RestrictedAt = DateTimeOffset.UtcNow;
        user.RestrictedBy = performedBy;
        user.RestrictionExpiresAt = duration.HasValue
            ? DateTimeOffset.UtcNow.Add(duration.Value)
            : null;

        user.ModifiedAt = DateTimeOffset.UtcNow;
        user.ModifiedBy = performedBy;
    }

    /// <summary>
    /// Confirmed fraud or compliance hold — suspend the account.
    /// Login is blocked. Existing transactions can still settle where the
    /// law requires it (settlement is a separate flow, gated on
    /// <see cref="CanReceivePayouts"/>).
    /// </summary>
    public static void SuspendForFraudOrCompliance(
        this User user,
        AccountRestrictionReason reason,
        string details,
        string performedBy,
        TimeSpan? duration = null)
    {
        if (reason is not (
            AccountRestrictionReason.SuspectedFraud
            or AccountRestrictionReason.ComplianceHold
            or AccountRestrictionReason.SuspiciousActivity
            or AccountRestrictionReason.AdministrativeAction))
        {
            throw new ArgumentException(
                $"Reason '{reason}' is not valid for suspension. " +
                $"Use SuspectedFraud, ComplianceHold, SuspiciousActivity " +
                $"or AdministrativeAction.",
                nameof(reason));
        }

        user.AccountStatus = UserAccountStatus.Suspended;
        user.RestrictionReason = reason;
        user.RestrictionReasonDetails = details;
        user.RestrictedAt = DateTimeOffset.UtcNow;
        user.RestrictedBy = performedBy;
        user.RestrictionExpiresAt = duration.HasValue
            ? DateTimeOffset.UtcNow.Add(duration.Value)
            : null;

        user.ModifiedAt = DateTimeOffset.UtcNow;
        user.ModifiedBy = performedBy;
    }

    /// <summary>
    /// Permanent account closure. Cannot be reversed by <see cref="Reactivate"/>.
    /// Outstanding funds must be settled under the closure policy BEFORE calling this.
    /// </summary>
    public static void ClosePermanently(
        this User user,
        AccountRestrictionReason reason,
        string details,
        string performedBy)
    {
        user.AccountStatus = UserAccountStatus.Closed;
        user.RestrictionReason = reason;
        user.RestrictionReasonDetails = details;
        user.RestrictedAt = DateTimeOffset.UtcNow;
        user.RestrictedBy = performedBy;
        user.RestrictionExpiresAt = null;

        // Soft-delete in the same transition.
        user.IsDeleted = true;
        user.DeletedAt = DateTimeOffset.UtcNow;
        user.DeletedBy = performedBy;

        user.ModifiedAt = DateTimeOffset.UtcNow;
        user.ModifiedBy = performedBy;
    }

    // ═════════════════════════════════════════════════════════
    // 4. Reactivation
    // ═════════════════════════════════════════════════════════

    /// <summary>
    /// Lift a restriction or suspension. Audit fields (reason, details,
    /// who restricted, when) are preserved — only status is changed.
    /// Throws if the account is Closed.
    /// </summary>
    public static void Reactivate(this User user, string performedBy)
    {
        if (user.AccountStatus == UserAccountStatus.Closed)
        {
            throw new InvalidOperationException(
                "A closed account cannot be reactivated. " +
                "Create a new account instead.");
        }

        user.AccountStatus = UserAccountStatus.Active;
        user.ReactivatedAt = DateTimeOffset.UtcNow;
        user.RestrictionExpiresAt = null;

        user.ModifiedAt = DateTimeOffset.UtcNow;
        user.ModifiedBy = performedBy;
    }

    // ═════════════════════════════════════════════════════════
    // 5. Auto-expiry
    // ═════════════════════════════════════════════════════════

    /// <summary>
    /// If a temporary restriction has expired, lift it in place.
    /// Returns true when the user was auto-reactivated.
    /// Intended for a background job that sweeps expired restrictions.
    /// </summary>
    public static bool TryAutoReactivate(this User user)
    {
        if (!user.HasExpiredRestriction()) return false;

        user.AccountStatus = UserAccountStatus.Active;
        user.ReactivatedAt = DateTimeOffset.UtcNow;
        user.RestrictionExpiresAt = null;
        user.ModifiedAt = DateTimeOffset.UtcNow;
        user.ModifiedBy = "system:auto-reactivation";

        return true;
    }

    // ═════════════════════════════════════════════════════════
    // 6. Display helpers
    // ═════════════════════════════════════════════════════════

    /// <summary>
    /// Short human-readable label for the account status.
    /// </summary>
    public static string GetStatusLabel(this User user) =>
        user.AccountStatus switch
        {
            UserAccountStatus.Active => "Active",
            UserAccountStatus.Restricted => "Restricted",
            UserAccountStatus.Suspended => "Suspended",
            UserAccountStatus.Deactivated => "Deactivated",
            UserAccountStatus.Closed => "Closed",
            _ => "Unknown",
        };

    /// <summary>
    /// Description used in admin/support views and in emails to the user.
    /// </summary>
    public static string GetStatusDescription(this User user) =>
        user.AccountStatus switch
        {
            UserAccountStatus.Active =>
                "Your account is in good standing and fully operational.",

            UserAccountStatus.Restricted =>
                "Some features are temporarily limited. Existing wallets and " +
                "releases continue normally.",

            UserAccountStatus.Suspended =>
                "Your account is suspended. Login is disabled while we review " +
                "the matter.",

            UserAccountStatus.Deactivated =>
                "Your account is deactivated. Contact support to reactivate it.",

            UserAccountStatus.Closed =>
                "Your account is permanently closed.",

            _ => "Unknown status.",
        };
}