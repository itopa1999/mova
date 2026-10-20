using System.Text.Json.Serialization;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    private async Task<CreateRecipientResult> CreateRecipientAsync(
        BankAccount bankAccount,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bankAccount.AccountName) ||
            string.IsNullOrWhiteSpace(bankAccount.AccountNumber) ||
            string.IsNullOrWhiteSpace(bankAccount.BankCode))
        {
            return new CreateRecipientResult
            {
                CallSucceeded = false,
                Message = "Account name, account number, and bank code are required."
            };
        }

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

        if (response is null)
        {
            return new CreateRecipientResult
            {
                CallSucceeded = false,
                Message = "No response from Paystack."
            };
        }

        if (!response.Status || response.Data is null ||
            string.IsNullOrWhiteSpace(response.Data.RecipientCode))
        {
            return new CreateRecipientResult
            {
                CallSucceeded = false,
                Message = response.Message ?? "Unable to create Paystack recipient."
            };
        }

        return new CreateRecipientResult
        {
            CallSucceeded = true,
            RecipientCode = response.Data.RecipientCode,
            Message = response.Message ?? "Recipient created."
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

    public sealed class CreateRecipientResult
    {
        public bool CallSucceeded { get; set; }
        public string? RecipientCode { get; set; }
        public string? Message { get; set; }
    }
}

// todo
// Check this before shipping:
// Create a recipient.
// Create the same recipient again with the same account number/bank code.
// Inspect the raw HTTP status and JSON.