using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Enums;

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
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
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

        if (response is null)
        {
            return new PaymentVerificationResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "No response from Flutterwave."
            };
        }

        if (string.Equals(response.Status, "error", StringComparison.OrdinalIgnoreCase)
            && response.Message?.Contains("No transaction", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new PaymentVerificationResult
            {
                CallSucceeded = true,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = response.Message
            };
        }

        if (!string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase)
            || response.Data is null)
        {
            return new PaymentVerificationResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = response.Message ?? "Unable to verify payment."
            };
        }

        if (!string.Equals(response.Data.TxRef, reference, StringComparison.Ordinal))
        {
            return new PaymentVerificationResult
            {
                CallSucceeded = false,
                Found = true,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Reference mismatch between request and Flutterwave response."
            };
        }

        var normalizedStatus =
            FlutterwavePaymentStatusMapper.NormalizeStatus(response.Data.Status);

        return new PaymentVerificationResult
        {
            CallSucceeded = true,
            Found = true,
            Status = normalizedStatus,
            Reference = response.Data.TxRef!,
            Amount = response.Data.Amount ?? 0m,
            Currency = response.Data.Currency,
            Message = BuildPaymentMessage(response.Data, normalizedStatus)
        };
    }

    private static string BuildPaymentMessage(
        FlutterwaveVerifyPaymentData data,
        TransactionStatus status)
    {
        return status switch
        {
            TransactionStatus.Completed =>
                $"Payment completed successfully. Amount: {data.Amount:N2} {data.Currency}.",

            TransactionStatus.Failed =>
                data.ProcessorResponse ?? "Payment failed.",

            TransactionStatus.Abandoned =>
                "Customer did not complete the payment.",

            TransactionStatus.Reversed =>
                "Payment was reversed (refund or chargeback).",

            TransactionStatus.Pending or TransactionStatus.Processing =>
                "Payment is still pending.",

            _ => data.ProcessorResponse ?? "Payment status unknown."
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

        [JsonPropertyName("processor_response")]
        public string? ProcessorResponse { get; set; }
    }
}