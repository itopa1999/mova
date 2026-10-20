using System.Text.Json.Serialization;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Payment;

public sealed partial class FlutterwaveService
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

        var currency = string.IsNullOrWhiteSpace(bankAccount.Currency)
            ? "NGN"
            : bankAccount.Currency!;

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/beneficiaries";

        var payload = new FlutterwaveCreateRecipientRequest
        {
            AccountNumber = bankAccount.AccountNumber,
            AccountBank = bankAccount.BankCode,
            BeneficiaryName = bankAccount.AccountName,
            Currency = currency
        };

        var response =
            await _externalApiClient.PostAsync<
                FlutterwaveCreateRecipientRequest,
                FlutterwaveCreateRecipientResponse>(
                    url,
                    payload,
                    headers,
                    cancellationToken);

        if (response is null)
        {
            return new CreateRecipientResult
            {
                CallSucceeded = false,
                Message = "No response from Flutterwave."
            };
        }

        if (!string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase)
            || response.Data is null
            || string.IsNullOrWhiteSpace(response.Data.Id))
        {
            return new CreateRecipientResult
            {
                CallSucceeded = false,
                Message = response.Message ?? "Unable to create Flutterwave recipient."
            };
        }

        return new CreateRecipientResult
        {
            CallSucceeded = true,
            RecipientId = response.Data.Id,
            Message = response.Message ?? "Recipient created."
        };
    }

    private sealed class FlutterwaveCreateRecipientRequest
    {
        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("account_bank")]
        public string AccountBank { get; set; } = string.Empty;

        [JsonPropertyName("beneficiary_name")]
        public string BeneficiaryName { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";
    }

    private sealed class FlutterwaveCreateRecipientResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public FlutterwaveRecipientData? Data { get; set; }
    }

    private sealed class FlutterwaveRecipientData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("bank_code")]
        public string? BankCode { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }
    }

    public sealed class CreateRecipientResult
    {
        public bool CallSucceeded { get; set; }
        public string? Message { get; set; }
        public string? RecipientId { get; set; }
    }
}