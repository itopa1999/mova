using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;

namespace Mova.Application.Interfaces.Payment;

public interface IPaystackService
{
    Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature);

    Task<ResolveBankAccountResult> ResolveBankAccountAsync(
        string accountNumber,
        string bankCode,
        CancellationToken cancellationToken = default);

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

    Task<PaystackCustomerResult> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        CancellationToken cancellationToken = default);

    Task<CreateDedicatedAccountResult> CreateDedicatedVirtualAccountAsync(
        string customerCode,
        string? firstName = null,
        string? lastName = null,
        string? phoneNumber = null,
        CancellationToken cancellationToken = default);
}


public sealed class PaystackCustomerResult
{
    public bool CallSucceeded { get; set; }
    public string? Message { get; set; }

    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}


public sealed class CreateDedicatedAccountResult
{
    public bool CallSucceeded { get; set; }
    public string? Message { get; set; }

    public string AccountId { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string CustomerCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAssigned { get; set; }
}