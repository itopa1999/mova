using Mova.Domain.Enums;

namespace Mova.Infrastructure.Payment;

public static class PaystackPaymentStatusMapper
{
    public static TransactionStatus NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return TransactionStatus.Pending;

        return status.Trim().ToLowerInvariant() switch
        {
            "success" or "successful" or "completed"
                => TransactionStatus.Completed,

            "failed" or "cancelled"
                => TransactionStatus.Failed,

            "abandoned"
                => TransactionStatus.Abandoned,

            "reversed"
                => TransactionStatus.Reversed,

            "processing"
                => TransactionStatus.Processing,

            "pending" or "ongoing" or "queued" or "new"
                => TransactionStatus.Pending,

            _ => TransactionStatus.Pending
        };
    }

    public static string NormalizeStatusString(string? status)
        => NormalizeStatus(status).ToString();
}

public static class PaystackTransferStatusMapper
{
    public static TransactionStatus NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return TransactionStatus.Pending;

        return status.Trim().ToLowerInvariant() switch
        {
            "otp" or "received"
                => TransactionStatus.Processing,

            "pending"
                => TransactionStatus.Pending,

            "success"
                => TransactionStatus.Completed,

            "failed" or "blocked" or "rejected"
                => TransactionStatus.Failed,

            "abandoned"
                => TransactionStatus.Abandoned,

            "reversed"
                => TransactionStatus.Reversed,

            _ => TransactionStatus.Pending
        };
    }

    public static string NormalizeStatusString(string? status)
        => NormalizeStatus(status).ToString();
}