using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class FlutterwaveService
{
    public async Task<PaymentVerificationResult> VerifyPaymentAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = false,
                Found = false,
                Status = "pending",
                Reference = reference,
                Message = "Reference is required."
            };
        }

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transactions/verify_by_reference" +
            $"?tx_ref={Uri.EscapeDataString(reference)}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var response =
            await _externalApiClient.GetAsync<FlutterwaveVerifyPaymentResponse>(
                url,
                headers,
                cancellationToken);

        Console.WriteLine("===== FLUTTERWAVE VERIFY RESPONSE4 =====");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
            response,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("====================================");

        if (response is null)
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = false,
                Found = false,
                Status = "pending",
                Reference = reference,
                Message = "No response from Flutterwave."
            };
        }

        if (string.Equals(response.Status, "error", StringComparison.OrdinalIgnoreCase)
            && response.Message?.Contains("No transaction", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = true,
                Found = false,
                Status = "not_found",
                Reference = reference,
                Message = response.Message
            };
        }

        if (!string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase)
            || response.Data is null)
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = false,
                Found = false,
                Status = "pending",
                Reference = reference,
                Message = response.Message ?? "Unable to verify payment."
            };
        }

        return new PaymentVerificationResult
        {
            IsSuccessful = true,
            Found = true,
            Status = FlutterwaveTransactionStatusMapper.NormalizeStatus(response.Data.Status),
            Reference = reference,
            Message = response.Message,
        };
    }

    private sealed class FlutterwaveVerifyPaymentResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public FlutterwaveVerifyPaymentData? Data { get; set; }
    }

    private sealed class FlutterwaveVerifyPaymentData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("tx_ref")]
        public string? TxRef { get; set; }

        [JsonPropertyName("flw_ref")]
        public string? FlwRef { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public decimal? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("charged_amount")]
        public decimal? ChargedAmount { get; set; }

        [JsonPropertyName("payment_type")]
        public string? PaymentType { get; set; }
    }
}
