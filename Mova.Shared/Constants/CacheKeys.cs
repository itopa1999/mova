namespace Mova.Shared.Constants;

public static class CacheKeys
{
    // ─────────────────────────────────────────────────────────────
    // Profile — CANONICAL key is by user id. The identifier index
    // (below) is a short-TTL pointer that resolves any identifier
    // (PublicId / email / phone) to a user id.
    // ─────────────────────────────────────────────────────────────
    public static string Profile(long userId) => $"profile:byid:{userId}";

    public const string ProfilePrefix = "profile:";

    /// <summary>
    /// Index entry: maps an identifier (PublicId, email, phone) to a user id.
    /// Short TTL, safe to be stale — the profile itself is always read by id.
    /// </summary>
    public static string ProfileIndex(string identifier) =>
        $"profile:idx:{identifier.Trim().ToLowerInvariant()}";

    // ─────────────────────────────────────────────────────────────
    // Banks
    // ─────────────────────────────────────────────────────────────
    public static string BanksAll() => "banks:all";

    public static string BanksSearch(string name) =>
        $"banks:search:{name.Trim().ToLowerInvariant()}";

    public static string BankByCode(string code) =>
        $"banks:code:{code.Trim().ToLowerInvariant()}";

    public static string BankBySlug(string slug) =>
        $"banks:slug:{slug.Trim().ToLowerInvariant()}";

    public const string BanksPrefix = "banks:";

    // ─────────────────────────────────────────────────────────────
    // Feature flags
    // ─────────────────────────────────────────────────────────────
    public static string FeatureFlags() => "feature_flags:map";
    public static string FeatureFlagsList() => "feature_flags:list";

    public const string FeatureFlagsPrefix = "feature_flags:";

    // ─────────────────────────────────────────────────────────────
    // Notifications
    // ─────────────────────────────────────────────────────────────
    public static string Notifications(string userPublicId, bool unreadOnly) =>
        $"notifications:{userPublicId.Trim().ToLowerInvariant()}:{(unreadOnly ? "unread" : "all")}";

    public static string NotificationsPrefix(string userPublicId) =>
        $"notifications:{userPublicId.Trim().ToLowerInvariant()}:";

    // ─────────────────────────────────────────────────────────────
    // Wallet
    // ─────────────────────────────────────────────────────────────
    public static string WalletTemplates() => "wallet_templates:all";
    public const string WalletTemplatesPrefix = "wallet_templates:";

    public static string WalletCategories() => "wallet_categories:all";
    public const string WalletCategoriesPrefix = "wallet_categories:";


    // ─────────────────────────────────────────────────────────────
// Home dashboard
// ─────────────────────────────────────────────────────────────
public static string HomeDashboard(string userPublicId) =>
    $"home:dashboard:{userPublicId.Trim().ToLowerInvariant()}";

public static string HomeDashboardPrefix(string userPublicId) =>
    $"home:dashboard:{userPublicId.Trim().ToLowerInvariant()}";

public const string HomeDashboardGlobalPrefix = "home:dashboard:";
}