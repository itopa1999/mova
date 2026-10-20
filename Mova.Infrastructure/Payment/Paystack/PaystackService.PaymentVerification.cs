using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    public async Task<TransferResult> VerifyTransferAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
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
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = "No response from Paystack."
            };
        }

        if (!response.Status || response.Data is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = response.Message ?? "Unable to verify transfer."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = FlutterwaveTransactionStatusMapper.NormalizeStatus(response.Data.Status),
            Reference = response.Data.Reference ?? reference,
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
