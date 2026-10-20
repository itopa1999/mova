using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    public async Task<TransferResult> VerifyTransferAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Reference is required."
            };
        }

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfer/verify/{Uri.EscapeDataString(reference)}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var response =
            await _externalApiClient.GetAsync<PaystackVerificationTransferResponse>(
                url,
                headers,
                cancellationToken);

        if (response is null)
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "No response from Paystack."
            };
        }

        if (!response.Status
            && response.Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new TransferResult
            {
                CallSucceeded = true,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = response.Message
            };
        }

        if (!response.Status || response.Data is null)
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = response.Message ?? "Unable to verify transfer."
            };
        }

        if (!string.Equals(response.Data.Reference, reference, StringComparison.Ordinal))
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Reference mismatch between request and Paystack response."
            };
        }

        return new TransferResult
        {
            CallSucceeded = true,
            Status = PaystackTransferStatusMapper.NormalizeStatus(response.Data.Status),
            Reference = response.Data.Reference,
            Message = response.Message ?? "Transfer verified."
        };
    }

    private sealed class PaystackVerificationTransferResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackTransferData? Data { get; set; }
    }
}