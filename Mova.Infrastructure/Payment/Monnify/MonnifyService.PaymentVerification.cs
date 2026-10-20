using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Enums;
using Mova.Infrastructure.Payment.Monnify;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService
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

        var token = await AuthenticateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PaymentVerificationResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Unable to authenticate with Monnify."
            };
        }

        var response = await _externalApiClient.GetAsync<MonnifyVerifyPaymentResponse>(
            $"{_settings.BaseUrl.TrimEnd('/')}/api/v2/merchant/transactions/query" +
            $"?paymentReference={Uri.EscapeDataString(reference)}",
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            cancellationToken);

        if (response is null)
        {
            return new PaymentVerificationResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "No response from Monnify."
            };
        }

        if (!response.RequestSuccessful || response.ResponseBody is null)
        {
            return new PaymentVerificationResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = response.ResponseMessage ?? "Unable to verify payment."
            };
        }

        if (!string.Equals(
                response.ResponseBody.PaymentReference,
                reference,
                StringComparison.Ordinal))
        {
            return new PaymentVerificationResult
            {
                CallSucceeded = false,
                Found = true,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Monnify returned a mismatched payment reference."
            };
        }

        var status =
            MonnifyTransactionStatusMapper.ToTransactionStatus(response.ResponseBody.PaymentStatus);

        return new PaymentVerificationResult
        {
            CallSucceeded = true,
            Found = true,
            Status = status,
            Reference = response.ResponseBody.PaymentReference,
            Amount = response.ResponseBody.AmountPaid,
            Currency = response.ResponseBody.CurrencyCode,
            Message = BuildMessage(response.ResponseBody, status)
        };
    }

    private static string BuildMessage(MonnifyVerifyPaymentBody body, TransactionStatus status)
    {
        return status switch
        {
            TransactionStatus.Completed when body.AmountPaid <= 0 =>
                "Payment reported as completed but amountPaid is zero — do not fulfil.",
            TransactionStatus.Completed =>
                $"Payment completed. Amount paid: {body.AmountPaid:N2} {body.CurrencyCode}.",
            TransactionStatus.Failed when string.Equals(
                body.PaymentStatus, "PARTIALLY_PAID", StringComparison.OrdinalIgnoreCase) =>
                $"Partially paid: {body.AmountPaid:N2} of {body.Amount:N2} received.",
            TransactionStatus.Failed =>
                "Payment failed.",
            TransactionStatus.Abandoned =>
                "Payment session expired.",
            TransactionStatus.Reversed =>
                "Payment was reversed.",
            TransactionStatus.Pending or TransactionStatus.Processing =>
                "Payment is still pending.",
            _ => "Payment status unknown."
        };
    }

    private sealed class MonnifyVerifyPaymentResponse
    {
        [JsonPropertyName("requestSuccessful")]
        public bool RequestSuccessful { get; set; }

        [JsonPropertyName("responseMessage")]
        public string? ResponseMessage { get; set; }

        [JsonPropertyName("responseCode")]
        public string? ResponseCode { get; set; }

        [JsonPropertyName("responseBody")]
        public MonnifyVerifyPaymentBody? ResponseBody { get; set; }
    }

    private sealed class MonnifyVerifyPaymentBody
    {
        [JsonPropertyName("paymentReference")]
        public string? PaymentReference { get; set; }

        [JsonPropertyName("transactionReference")]
        public string? TransactionReference { get; set; }

        [JsonPropertyName("paymentStatus")]
        public string? PaymentStatus { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("amountPaid")]
        public decimal AmountPaid { get; set; }

        [JsonPropertyName("currencyCode")]
        public string? CurrencyCode { get; set; }

        [JsonPropertyName("paymentMethod")]
        public string? PaymentMethod { get; set; }
    }
}