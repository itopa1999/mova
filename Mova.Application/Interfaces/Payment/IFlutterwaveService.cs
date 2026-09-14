using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;

namespace Mova.Application.Interfaces.Payment;

public interface IFlutterwaveService
{
    Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature);

    Task<PaymentInitializationResultDto> InitializePaymentAsync(
        string email,
        decimal amount,
        string reference,
        CancellationToken cancellationToken);

    Task<TransferResult> TransferAsync(
        BankAccount bankAccount,
        Money amount,
        string reference,
        CancellationToken cancellationToken = default);

    Task<TransferResult> VerifyTransferAsync(
        string reference,
        CancellationToken cancellationToken = default);

    Task<PaymentVerificationResult> VerifyPaymentAsync(
        string reference,
        CancellationToken cancellationToken);
}
