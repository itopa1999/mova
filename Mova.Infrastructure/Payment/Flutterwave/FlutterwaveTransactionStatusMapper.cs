using Mova.Domain.Enums;

namespace Mova.Infrastructure.Payment;

public static class FlutterwavePaymentStatusMapper
{
    public static TransactionStatus NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return TransactionStatus.Pending;

        return status.Trim().ToLowerInvariant() switch
        {
            "successful" or "success" or "completed"
                => TransactionStatus.Completed,

            "failed" or "cancelled"
                => TransactionStatus.Failed,

            "abandoned"
                => TransactionStatus.Abandoned,

            "reversed"
                => TransactionStatus.Reversed,

            "pending" or "new" or "processing" or "queued"
                => TransactionStatus.Pending,

            _ => TransactionStatus.Pending
        };
    }

    public static string NormalizeStatusString(string? status)
        => NormalizeStatus(status).ToString();
}

public static class FlutterwaveTransferStatusMapper
{
    public static TransactionStatus NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return TransactionStatus.Pending;

        return status.Trim().ToUpperInvariant() switch
        {
            "SUCCESSFUL" => TransactionStatus.Completed,
            "FAILED" => TransactionStatus.Failed,
            "CANCELLED" => TransactionStatus.Abandoned,
            "REVERSED" => TransactionStatus.Reversed,
            "NEW" or "PENDING" or "PROCESSING" or "QUEUED" => TransactionStatus.Pending,
            _ => TransactionStatus.Pending
        };
    }
}