namespace Mova.Application.Interfaces.Security;

public interface ITransactionPinAttemptStore
{
    Task<bool> IsLockedAsync(
        string userPublicId,
        CancellationToken cancellationToken = default);

    Task<bool> RecordFailureAsync(
        string userPublicId,
        CancellationToken cancellationToken = default);

    Task<bool> ResetAfterSuccessfulVerificationAsync(
        string userPublicId,
        CancellationToken cancellationToken = default);

    Task ResetAsync(
        string userPublicId,
        CancellationToken cancellationToken = default);
}
