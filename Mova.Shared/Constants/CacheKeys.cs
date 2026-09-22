namespace Mova.Shared.Constants;

public static class CacheKeys
{
    public static string Profile(long userId) => $"profile:{userId}";
    public static string ProfileByIdentifier(string identifier) =>
        $"profile:{identifier.Trim().ToLowerInvariant()}";

    public static string ProfilePrefix(string publicId) =>
        $"profile:{publicId}";

    public static string BanksAll() => "banks:all";

    public static string BanksSearch(string name) =>
        $"banks:search:{name.Trim().ToLowerInvariant()}";

    public static string BankByCode(string code) =>
        $"banks:code:{code.Trim().ToLowerInvariant()}";

    public static string BankBySlug(string slug) =>
        $"banks:slug:{slug.Trim().ToLowerInvariant()}";

    public const string BanksPrefix = "banks:";

    public static string FeatureFlags() => "feature_flags:map";
    public static string FeatureFlagsList() => "feature_flags:list";

    public const string FeatureFlagsPrefix = "feature_flags:";

    public static string Notifications(string userPublicId, bool unreadOnly) =>
        $"notifications:{userPublicId.Trim().ToLowerInvariant()}:{(unreadOnly ? "unread" : "all")}";

    public static string NotificationsPrefix(string userPublicId) =>
        $"notifications:{userPublicId.Trim().ToLowerInvariant()}:";

    public static string WalletTemplates() => "wallet_templates:all";

    public const string WalletTemplatesPrefix = "wallet_templates:";

    public static string WalletCategories() => "wallet_categories:all";

    public const string WalletCategoriesPrefix = "wallet_categories:";
}
