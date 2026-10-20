using Mova.Domain.Enums;

namespace Mova.Application.Interfaces.Payment;

public sealed class PaymentVerificationResult
{
    public bool CallSucceeded { get; set; }
    public bool Found { get; set; }
    public TransactionStatus Status { get; set; } = TransactionStatus.Pending;
    public string? Reference { get; set; }
    public string? Message { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
}
