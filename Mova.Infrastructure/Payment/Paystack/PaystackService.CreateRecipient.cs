

using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    private async Task<TransferResult> CreateRecipientAsync(
        BankAccount bankAccount,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transferrecipient";

        var payload = new PaystackCreateRecipientRequest
        {
            Type = "nuban",
            Name = bankAccount.AccountName,
            AccountNumber = bankAccount.AccountNumber,
            BankCode = bankAccount.BankCode,
            Currency = string.IsNullOrWhiteSpace(bankAccount.Currency)
                ? "NGN"
                : bankAccount.Currency!
        };

        var response =
            await _externalApiClient.PostAsync<PaystackCreateRecipientRequest, PaystackCreateRecipientResponse>(
                url,
                payload,
                headers,
                cancellationToken);

        if (response is null || !response.Status || response.Data is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = string.Empty,
                Message = response?.Message ?? "Unable to create Paystack recipient."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = "success",
            Reference = response.Data.RecipientCode,
            Message = "Recipient created."
        };
    }

    private sealed class PaystackCreateRecipientRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "nuban";

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("bank_code")]
        public string BankCode { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";
    }

    private sealed class PaystackCreateRecipientResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackCreateRecipientData? Data { get; set; }
    }

    private sealed class PaystackCreateRecipientData
    {
        [JsonPropertyName("recipient_code")]
        public string RecipientCode { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("bank_code")]
        public string BankCode { get; set; } = string.Empty;
    }
}