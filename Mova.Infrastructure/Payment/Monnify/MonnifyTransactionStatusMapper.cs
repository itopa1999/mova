using Mova.Domain.Enums;

namespace Mova.Infrastructure.Payment.Monnify;

/// <summary>Maps Monnify collection statuses to MOVA's transaction lifecycle.</summary>
public static class MonnifyTransactionStatusMapper
{
    public static TransactionStatus ToTransactionStatus(string? paymentStatus)
    {
        if (string.IsNullOrWhiteSpace(paymentStatus))
            return TransactionStatus.Pending;

        return paymentStatus.Trim().ToUpperInvariant() switch
        {
            "PAID" or "OVERPAID" => TransactionStatus.Completed,
            "PROCESSING" or "IN_PROGRESS" => TransactionStatus.Processing,
            "FAILED" or "CANCELLED" or "EXPIRED" => TransactionStatus.Failed,
            "REVERSED" or "REFUNDED" => TransactionStatus.Reversed,
            "PENDING" or "PARTIALLY_PAID" => TransactionStatus.Pending,
            _ => TransactionStatus.Pending
        };
    }

    public static string ToLegacyResultStatus(TransactionStatus status) => status switch
    {
        TransactionStatus.Completed => "success",
        TransactionStatus.Failed => "failed",
        TransactionStatus.Reversed => "reversed",
        TransactionStatus.Processing => "processing",
        _ => "pending"
    };
}
