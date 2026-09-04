using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Application.Interfaces.Service;

public interface IWalletRuleValidator
{
    /// <summary>
    /// Validates a wallet rule for a NEW wallet creation
    /// </summary>
    Task<ValidationResult> ValidateForNewWalletAsync(WalletRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a wallet rule for an EXISTING wallet
    /// </summary>
    Task<ValidationResult> ValidateForExistingWalletAsync(WalletRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates only the frequency configuration
    /// </summary>
    Task<ValidationResult> ValidateConfigAsync(ReleaseFrequency type, string configJson, CancellationToken cancellationToken = default);
}