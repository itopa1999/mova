namespace Mova.Application.Interfaces.Payment;

public interface IMonnifyService
{
    Task<PaymentInitializationResultDto> InitializePaymentAsync(
        string email,
        decimal amount,
        string reference,
        CancellationToken cancellationToken);

    // Task<PaymentVerificationResult> VerifyPaymentAsync(
    //     string reference,
    //     CancellationToken cancellationToken);
}
