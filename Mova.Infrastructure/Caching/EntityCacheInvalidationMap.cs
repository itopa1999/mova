
using Mova.Domain.Entities;
using Mova.Infrastructure.Identity;
using Mova.Shared.Constants;

namespace Mova.Infrastructure.Caching;

public static class EntityCacheInvalidationMap
{
    private static readonly IReadOnlyDictionary<Type, string[]> Prefixes =
        new Dictionary<Type, string[]>
        {
            // User profile cache
            [typeof(User)] =
            [
                CacheKeys.ProfilePrefix
            ],

            // Bank list, search, code and slug caches
            [typeof(Bank)] =
            [
                CacheKeys.BanksPrefix
            ],

            // Feature flag caches
            [typeof(FeatureFlag)] =
            [
                CacheKeys.FeatureFlagsPrefix
            ],

            // Wallet template caches
            [typeof(WalletTemplate)] =
            [
                CacheKeys.WalletTemplatesPrefix
            ],

            // Wallet category caches
            [typeof(WalletCategory)] =
            [
                CacheKeys.WalletCategoriesPrefix
            ],

            // Notification caches
            [typeof(AppNotification)] =
            [
                "notifications:"
            ]
        };

    public static IReadOnlyCollection<string> GetPrefixes(Type entityType)
    {
        return Prefixes.TryGetValue(entityType, out var prefixes)
            ? prefixes
            : Array.Empty<string>();
    }
}
