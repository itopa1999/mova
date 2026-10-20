using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;

namespace Mova.Infrastructure.Payment;

public sealed partial class FlutterwaveService
{
    public async Task<TransferResult> TransferAsync(
        BankAccount bankAccount,
        Money amount,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        // Step 1 — create a recipient if we don't already have one.
        var recipientId = bankAccount.FlutterwaveRecipientId;

        if (string.IsNullOrWhiteSpace(recipientId))
        {
            var recipientResult = await CreateRecipientAsync(
                bankAccount, headers, cancellationToken);

            if (!recipientResult.IsSuccessful)
                return recipientResult;

            recipientId = recipientResult.Reference;

            bankAccount.FlutterwaveRecipientId = recipientId;
        }

        // Step 2 — initiate the transfer.
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfers";

        var payload = new FlutterwaveTransferRequest
        {
            AccountBank = bankAccount.BankCode,
            AccountNumber = bankAccount.AccountNumber,
            Narration = $"Payout {reference}",
            Currency = string.IsNullOrWhiteSpace(bankAccount.Currency)
                ? "NGN"
                : bankAccount.Currency!,
            Amount = amount.ToDecimal(),
            Reference = reference,
            CallbackUrl = _externalApiSettings.PaymentCallbackUrl
        };

        var response =
            await _externalApiClient.PostAsync<
                FlutterwaveTransferRequest,
                FlutterwaveTransferResponse>(
                    url,
                    payload,
                    headers,
                    cancellationToken);

        if (response is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = "No response from Flutterwave."
            };
        }

        if (response.Status != "success" || response.Data is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = response.Message ?? "Unable to initiate transfer."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = FlutterwaveTransactionStatusMapper.NormalizeStatus(response.Data.Status),
            Reference = response.Data.Reference ?? reference,
            Message = response.Message ?? "Transfer initiated."
        };
    }

    public async Task<TransferResult> VerifyTransferAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var query =
            $"?reference={Uri.EscapeDataString(reference)}";

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfers{query}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var response =
            await _externalApiClient.GetAsync<FlutterwaveTransferListResponse>(
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
                Message = "No response from Flutterwave."
            };
        }

        if (response.Status != "success"
            || response.Data is null
            || response.Data.Count == 0)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = response.Message ?? "Transfer not found."
            };
        }

        var transfer = response.Data[0];

        return new TransferResult
        {
            IsSuccessful = true,
            Status = FlutterwaveTransactionStatusMapper.NormalizeStatus(transfer.Status),
            Reference = transfer.Reference ?? reference,
            Message = response.Message ?? "Transfer verified."
        };
    }

    private sealed class FlutterwaveTransferRequest
    {
        [JsonPropertyName("account_bank")]
        public string AccountBank { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("narration")]
        public string Narration { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;

        [JsonPropertyName("callback_url")]
        public string? CallbackUrl { get; set; }
    }

    private sealed class FlutterwaveTransferResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public FlutterwaveTransferData? Data { get; set; }
    }

    private sealed class FlutterwaveTransferData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("complete_message")]
        public string? CompleteMessage { get; set; }
    }

    private sealed class FlutterwaveTransferListResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public List<FlutterwaveTransferData>? Data { get; set; }
    }
}
