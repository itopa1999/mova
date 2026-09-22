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
    private static readonly TimeSpan FlagsCacheTtl = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _context;
    private readonly ICacheService _cache;

    public FeatureFlagService(
        ApplicationDbContext context,
        ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<bool> IsEnabledAsync(
        FeatureFlagName featureFlagName,
        CancellationToken cancellationToken = default)
    {
        var flags = await GetFlagsMapAsync(cancellationToken);

        return flags.TryGetValue(featureFlagName, out var enabled)
               && enabled;
    }

    public async Task<List<FeatureFlagSnapshot>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var flags = await _cache.GetOrSetFastAsync(
            CacheKeys.FeatureFlagsList(),
            LoadFlagsListFromDbAsync,
            timeout: FlagsCacheTtl,
            cancellationToken: cancellationToken);

        return flags ?? new List<FeatureFlagSnapshot>();
    }

    private async Task<Dictionary<FeatureFlagName, bool>> GetFlagsMapAsync(
        CancellationToken cancellationToken)
    {
        var flags = await _cache.GetOrSetFastAsync(
            CacheKeys.FeatureFlags(),
            LoadFlagsMapFromDbAsync,
            timeout: FlagsCacheTtl,
            cancellationToken: cancellationToken);

        return flags ?? new Dictionary<FeatureFlagName, bool>();
    }

    private async Task<Dictionary<FeatureFlagName, bool>> LoadFlagsMapFromDbAsync(
        CancellationToken cancellationToken)
    {
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