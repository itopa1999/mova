using Mova.Domain.Enums;

namespace Mova.Application.Interfaces.Service;

public interface IFeatureFlagService
{
Task<bool> IsEnabledAsync(FeatureFlagName featureFlagName, CancellationToken cancellationToken = default);

}