using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;

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
        var flags = await GetFlagsAsync(cancellationToken);

        return flags.TryGetValue(featureFlagName, out var enabled)
               && enabled;
    }

    private async Task<Dictionary<FeatureFlagName, bool>> GetFlagsAsync(
        CancellationToken cancellationToken)
    {
        return await _context.FeatureFlags
        .AsNoTracking()
        .ToDictionaryAsync(
            x => x.Name,
            x => x.IsEnabled,
            cancellationToken);
    }
}