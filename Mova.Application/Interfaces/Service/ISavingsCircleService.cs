
using Mova.Domain.Enums;

namespace Mova.Application.Interfaces.Service;

public interface ISavingsCircleService
{
    Task<(bool Success, string Message)> ValidateFrequencyAsync(
        ContributionFrequency  frequency,
        string frequencyConfig,
        CancellationToken cancellationToken);

    string HumanizeFrequency(
        ContributionFrequency frequency,
        string frequencyConfig);
}