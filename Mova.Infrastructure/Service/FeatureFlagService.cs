
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Constants;
using static Mova.Application.Interfaces.Service.IFeatureFlagService;

namespace Mova.Infrastructure.Services;

public sealed class FeatureFlagService : IFeatureFlagService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(1);

    private readonly ApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public FeatureFlagService(
        ApplicationDbContext context,
        ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<bool> IsEnabledAsync(
        FeatureFlagName featureFlagName,
        CancellationToken cancellationToken = default)
    {
        var flags = await _cacheService.GetOrSetAsync(
            CacheKeys.FeatureFlags(),
            LoadFlagsMapFromDbAsync,
            timeout: CacheDuration,
            cancellationToken: cancellationToken);

        return flags is not null
               && flags.TryGetValue(featureFlagName, out var enabled)
               && enabled;
    }

    public async Task<List<FeatureFlagSnapshot>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var flags = await _cacheService.GetOrSetAsync(
            CacheKeys.FeatureFlagsList(),
            LoadFlagsListFromDbAsync,
            timeout: CacheDuration,
            cancellationToken: cancellationToken);

        return flags ?? [];
    }

    private async Task<Dictionary<FeatureFlagName, bool>> LoadFlagsMapFromDbAsync(
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            "[DATABASE LOAD] Loading feature-flag map from the database.");

        return await _context.FeatureFlags
            .AsNoTracking()
            .ToDictionaryAsync(
                x => x.Name,
                x => x.IsEnabled,
                cancellationToken);
    }

    private async Task<List<FeatureFlagSnapshot>> LoadFlagsListFromDbAsync(
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            "[DATABASE LOAD] Loading feature-flag list from the database.");

        return await _context.FeatureFlags
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new FeatureFlagSnapshot(
                x.Id,
                x.Name,
                x.Description,
                x.IsEnabled,
                x.Metadata,
                x.CreatedAt,
                x.ModifiedAt))
            .ToListAsync(cancellationToken);
    }
}
