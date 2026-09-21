using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Application.Interfaces.Service;

public interface IRenewalService
{
    Task<RenewalOutcome> TryRenewWalletAsync(
        long walletId,
        RenewalTriggerType firedBy,
        CancellationToken cancellationToken);
}

public sealed class RenewalOutcome
{
    public bool Executed { get; init; }
    public bool Skipped { get; init; }
    public string? SkipReason { get; init; }
    public long? RenewalEventId { get; init; }
    public Money? RefilledAmount { get; init; }
}