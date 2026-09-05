using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;

namespace Mova.Application.Interfaces.Service;

public interface IWalletRuleService
{
    Task<NextWalletRelease?> GetNextReleaseAsync(
        WalletRule rule,
        DateTimeOffset after,
        CancellationToken cancellationToken = default);
}

public sealed class NextWalletRelease
{
    public DateTimeOffset ScheduledFor { get; init; }

    public Money Amount { get; init; }
}
