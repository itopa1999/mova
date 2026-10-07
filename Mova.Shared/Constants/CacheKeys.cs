namespace Mova.Shared.Constants;

public static class CacheKeys
{
    // ── Profile ────────────────────────────────────────────────
    public static string Profile(long userId) => $"profile:byid:{userId}";
    public const string ProfilePrefix = "profile:";
    public static string ProfileIndex(string identifier) =>
        $"profile:idx:{Norm(identifier)}";

    // ── Banks ──────────────────────────────────────────────────
    public static string BanksAll() => "banks:all";
    public static string BanksSearch(string name) =>
        $"banks:search:{Norm(name)}";
    public static string BankByCode(string code) =>
        $"banks:code:{Norm(code)}";
    public static string BankBySlug(string slug) =>
        $"banks:slug:{Norm(slug)}";
    public const string BanksPrefix = "banks:";

    // ── Feature flags ──────────────────────────────────────────
    public static string FeatureFlags() => "feature_flags:map";
    public static string FeatureFlagsList() => "feature_flags:list";
    public const string FeatureFlagsPrefix = "feature_flags:";

    // ── Notifications ──────────────────────────────────────────
    public static string Notifications(string userPublicId, bool unreadOnly) =>
        $"notifications:{Norm(userPublicId)}:{(unreadOnly ? "unread" : "all")}";
    public static string NotificationsPrefix(string userPublicId) =>
        $"notifications:{Norm(userPublicId)}:";

    // ── Wallet ─────────────────────────────────────────────────
    public static string WalletTemplates() => "wallet_templates:all";
    public const string WalletTemplatesPrefix = "wallet_templates:";
    public static string WalletCategories() => "wallet_categories:all";
    public const string WalletCategoriesPrefix = "wallet_categories:";

    // ── Home dashboard ─────────────────────────────────────────
    public static string HomeDashboard(string userPublicId) =>
        $"home:dashboard:{Norm(userPublicId)}";
    public static string HomeDashboardPrefix(string userPublicId) =>
        $"home:dashboard:{Norm(userPublicId)}";
    public const string HomeDashboardGlobalPrefix = "home:dashboard:";

    // ── Transaction PIN ────────────────────────────────────────
    // Prefix matches the method name (TransactionPin*) so that the key
    // visible in Redis matches the code that produced it.
    private const string TransactionPinPrefix = "mova:security:transaction-pin";

    public static string TransactionPinAttempts(string userPublicId) =>
        $"{TransactionPinPrefix}:attempts:{Norm(userPublicId)}";

    public static string TransactionPinLock(string userPublicId) =>
        $"{TransactionPinPrefix}:lock:{Norm(userPublicId)}";

    public static string TransactionPinPrefixGlobal() => TransactionPinPrefix;

    private static string Norm(string s) => s.Trim().ToLowerInvariant();
}