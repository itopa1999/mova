namespace Mova.Application.Interfaces.Security;

public interface ITransactionPinService
{
    Task<bool> HasPinAsync(
        string UserPublicId,
        CancellationToken cancellationToken = default);

    Task SetPinAsync(
        string UserPublicId,
        string pin,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyPinAsync(
        string UserPublicId,
        string pin,
        CancellationToken cancellationToken = default);

    Task ChangePinAsync(
        string UserPublicId,
        string newPin,
        CancellationToken cancellationToken = default);
}