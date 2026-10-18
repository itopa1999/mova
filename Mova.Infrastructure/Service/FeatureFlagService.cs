using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;
using static Mova.Application.Interfaces.Service.IFeatureFlagService;

namespace Mova.Infrastructure.Services;

public sealed class FeatureFlagService : IFeatureFlagService
{
    private readonly ApplicationDbContext _context;

    public FeatureFlagService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsEnabledAsync(
        FeatureFlagName featureFlagName,
        CancellationToken cancellationToken = default)
    {
        var flags = await LoadFlagsMapFromDbAsync(cancellationToken);

        return flags.TryGetValue(featureFlagName, out var enabled)
               && enabled;
    }

    public async Task<List<FeatureFlagSnapshot>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await LoadFlagsListFromDbAsync(cancellationToken);
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