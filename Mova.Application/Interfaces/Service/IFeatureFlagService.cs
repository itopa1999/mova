using Mova.Domain.Enums;

namespace Mova.Application.Interfaces.Service;

public interface IFeatureFlagService
{
Task<bool> IsEnabledAsync(FeatureFlagName featureFlagName, CancellationToken cancellationToken = default);

Task<List<FeatureFlagSnapshot>> GetAllAsync(
        CancellationToken cancellationToken = default);

public sealed record FeatureFlagSnapshot(
    long Id,
    FeatureFlagName Name,
    string Description,
    bool IsEnabled,
    string? Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);
}