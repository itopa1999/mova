namespace Mova.Api.RateLimiting;

public static class RateLimitPolicies
{
    /// <summary>
    /// Strict limits for brute-forceable endpoints:
    /// login, register, verify-email, resend-otp, forgot-password.
    /// </summary>
    public const string AuthStrict = "auth-strict";

    /// <summary>
    /// Medium limits for auth endpoints that run silently:
    /// refresh-token, logout.
    /// </summary>
    public const string AuthMedium = "auth-medium";

    /// <summary>
    /// Generous limits for read-only endpoints:
    /// GET /home, /wallets, /releases, etc.
    /// </summary>
    public const string Read = "read";

    /// <summary>
    /// Moderate limits for write endpoints that don't move money:
    /// create-wallet, update-profile, etc.
    /// </summary>
    public const string Write = "write";

    /// <summary>
    /// Very strict limits for endpoints that move money or change
    /// security-sensitive state: set-pin, change-pin, break-wallet,
    /// unused-money, fund, payouts.
    /// </summary>
    public const string Sensitive = "sensitive";
}