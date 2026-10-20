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

            "PARTIALLY_PAID" => TransactionStatus.Failed,

            "EXPIRED" => TransactionStatus.Abandoned,

            "FAILED" or "CANCELLED" => TransactionStatus.Failed,

            "REVERSED" or "REFUNDED" => TransactionStatus.Reversed,

            "PENDING" => TransactionStatus.Pending,

            _ => TransactionStatus.Pending
        };
    }
}

/// <summary>
/// Maps Monnify disbursement (transfer) statuses to MOVA's transaction lifecycle.
/// Source: /api/v2/disbursements/single/summary -> responseBody.status
/// </summary>
public static class MonnifyTransferStatusMapper
{
    public static TransactionStatus ToTransactionStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return TransactionStatus.Pending;

        return status.Trim().ToUpperInvariant() switch
        {
            "SUCCESS" => TransactionStatus.Completed,

            "FAILED" => TransactionStatus.Failed,

            "REVERSED" => TransactionStatus.Reversed,

            // MFA enabled (default): OTP must be submitted before processing.
            "PENDING_AUTHORIZATION" => TransactionStatus.Processing,

            // OTP email could not be delivered; transfer will not proceed automatically.
            "OTP_EMAIL_DISPATCH_FAILED" => TransactionStatus.Failed,

            "PENDING" => TransactionStatus.Pending,

            _ => TransactionStatus.Pending
        };
    }
}