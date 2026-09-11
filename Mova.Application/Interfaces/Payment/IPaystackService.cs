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

    Task<PaymentInitializationResultDto> InitializePaymentAsync(
        string email,
        decimal amount,
        string reference,
        CancellationToken cancellationToken);

    // Task<PaymentVerificationResult> VerifyPaymentAsync(
    //     string reference,
    //     CancellationToken cancellationToken);
}
