

using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;

namespace Mova.Infrastructure.Payment;

public sealed partial class FlutterwaveService
{
    private async Task<TransferResult> CreateRecipientAsync(
        BankAccount bankAccount,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfers/recipients";

        var payload = new FlutterwaveCreateRecipientRequest
        {
            Type = "bank",
            Name = bankAccount.AccountName,
            AccountNumber = bankAccount.AccountNumber,
            BankCode = bankAccount.BankCode,
            Currency = string.IsNullOrWhiteSpace(bankAccount.Currency)
                ? "NGN"
                : bankAccount.Currency!
        };

        var response =
            await _externalApiClient.PostAsync<
                FlutterwaveCreateRecipientRequest,
                FlutterwaveCreateRecipientResponse>(
                    url,
                    payload,
                    headers,
                    cancellationToken);

        if (response is null
            || response.Status != "success"
            || response.Data is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = string.Empty,
                Message = response?.Message
                    ?? "Unable to create Flutterwave recipient."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = "success",
            Reference = response.Data.Id.ToString(),
            Message = "Recipient created."
        };
    }


    private sealed class FlutterwaveCreateRecipientRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "bank";

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("bank_code")]
        public string BankCode { get; set; } = string.Empty;

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
        public long Id { get; set; }

        [JsonPropertyName("account_number")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("bank_code")]
        public string? BankCode { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }
    }
}