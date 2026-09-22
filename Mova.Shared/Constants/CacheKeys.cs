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
}
