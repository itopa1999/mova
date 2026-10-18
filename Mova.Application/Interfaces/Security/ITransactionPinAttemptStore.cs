namespace Mova.Application.Interfaces.Security;

public interface ITransactionPinAttemptStore
{
    Task<bool> IsLockedAsync(
        string userPublicId,
        CancellationToken cancellationToken = default);

    Task<TransactionPinFailureOutcome> RecordFailureAsync(
        string userPublicId,
        CancellationToken cancellationToken = default);

    Task<bool> ResetAfterSuccessfulVerificationAsync(
        string userPublicId,
        CancellationToken cancellationToken = default);

    Task ResetAsync(
        string userPublicId,
        CancellationToken cancellationToken = default);
}


public enum TransactionPinFailureOutcome
{
    None,
    Warning,
    LockedNow,
}