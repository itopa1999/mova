using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
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
            $"{_settings.BaseUrl.TrimEnd('/')}/transaction/verify/{Uri.EscapeDataString(reference)}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var response =
            await _externalApiClient.GetAsync<PaystackVerifyPaymentResponse>(
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
                Message = "No response from Paystack."
            };
        }

        if (!response.Status
            && response.Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
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

        if (!response.Status || response.Data is null)
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

        if (!string.Equals(response.Data.Reference, reference, StringComparison.Ordinal))
        {
            return new PaymentVerificationResult
            {
                CallSucceeded = false,
                Found = true,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Reference mismatch between request and Paystack response."
            };
        }

        var normalizedStatus =
            PaystackPaymentStatusMapper.NormalizeStatus(response.Data.Status);

        return new PaymentVerificationResult
        {
            CallSucceeded = true,
            Found = true,
            Status = normalizedStatus,
            Reference = response.Data.Reference,
            Amount = response.Data.Amount,
            Currency = response.Data.Currency,
            Message = BuildPaymentMessage(response.Data, normalizedStatus)
        };
    }

    private static string BuildPaymentMessage(
        PaystackVerifyPaymentData data,
        TransactionStatus status)
    {
        return status switch
        {
            TransactionStatus.Completed =>
                $"Payment completed successfully. Amount: {(data.Amount / 100m):N2} {data.Currency}.",
            TransactionStatus.Failed =>
                data.GatewayResponse ?? "Payment failed.",
            TransactionStatus.Abandoned =>
                "Customer did not complete the payment.",
            TransactionStatus.Reversed =>
                "Payment was reversed (refund or chargeback).",
            TransactionStatus.Pending or TransactionStatus.Processing =>
                "Payment is still pending.",
            _ => data.GatewayResponse ?? "Payment status unknown."
        };
    }

    private sealed class PaystackVerifyPaymentResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackVerifyPaymentData? Data { get; set; }
    }

    private sealed class PaystackVerifyPaymentData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("paid_at")]
        public string? PaidAt { get; set; }

        [JsonPropertyName("channel")]
        public string? Channel { get; set; }

        [JsonPropertyName("gateway_response")]
        public string? GatewayResponse { get; set; }

        [JsonPropertyName("customer")]
        public PaystackVerifyPaymentCustomer? Customer { get; set; }
    }

    private sealed class PaystackVerifyPaymentCustomer
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("customer_code")]
        public string? CustomerCode { get; set; }
    }
}