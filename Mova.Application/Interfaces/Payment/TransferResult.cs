using Mova.Domain.Enums;

namespace Mova.Application.Interfaces.Payment;

public sealed class TransferResult
{
    public bool CallSucceeded { get; set; }
    public bool Found { get; set; }
    public TransactionStatus Status { get; set; } = TransactionStatus.Pending;
    public string Reference { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}