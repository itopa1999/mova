
namespace Mova.Infrastructure.Payment;
public static class PaystackTransactionStatusMapper
{
    public static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "pending";

        return status.ToLowerInvariant() switch
        {
            "successful" => "success",
            "success" => "success",
            "completed" => "success",
            "failed" => "failed",
            "cancelled" => "failed",
            "reversed" => "reversed",
            "pending" => "pending",
            "new" => "pending",
            "processing" => "pending",
            "queued" => "pending",
            _ => "pending"
        };
    }
}
