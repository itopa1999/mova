namespace Mova.Application.Interfaces.Payment;

public interface IPaystackService
{
    Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature);

    Task<ResolveBankAccountResponse?> ResolveBankAccountAsync(
        string accountNumber,
        string bankCode,
        CancellationToken cancellationToken = default);
}
