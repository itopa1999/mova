namespace Mova.Shared.Constants;

public static class CacheKeys
{
    // ── Profile ────────────────────────────────────────────────
    public static string Profile(long userId) => $"profile:byid:{userId}";
    public static string ProfileIndex(string identifier) =>
        $"profile:idx:{Norm(identifier)}";
    public const string ProfilePrefix = "profile:";


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



    // ── Wallet Templates ────────────────────────────────────────────────
    public static string WalletTemplates(
        int page,
        int pageSize,
        string? search,
        long? categoryId,
        string? walletType)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search)
            ? string.Empty
            : search.Trim().ToLowerInvariant();

        var normalizedWalletType = string.IsNullOrWhiteSpace(walletType)
            ? "any"
            : walletType.Trim().ToLowerInvariant();

        return $"wallet_templates:page:{page}" +
            $":size:{pageSize}" +
            $":search:{Uri.EscapeDataString(normalizedSearch)}" +
            $":category:{categoryId?.ToString() ?? "any"}" +
            $":type:{normalizedWalletType}";
    }

    public const string WalletTemplatesPrefix = "wallet_templates:";


    // ── Wallet Categories ─────────────────────────────────────────────────
    public static string WalletCategories() => "wallet_categories:all";
    public const string WalletCategoriesPrefix = "wallet_categories:";


    // ── Wallet Trustees ───────────────────────────────────────────────────
    public static string OwnerTrustees(string ownerPublicId) =>
        $"wallet_trustees:owner:{Norm(ownerPublicId)}";
    public const string WalletTrusteesPrefix = "wallet_trustees:";
    

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