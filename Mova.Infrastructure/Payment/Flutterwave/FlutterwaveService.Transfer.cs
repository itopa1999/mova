using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
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

        if (amount.ToDecimal() <= 0)
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Amount must be greater than zero."
            };
        }

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var recipientId = bankAccount.FlutterwaveRecipientId;

        if (string.IsNullOrWhiteSpace(recipientId))
        {
            var recipientResult = await CreateRecipientAsync(
                bankAccount, headers, cancellationToken);

            if (!recipientResult.CallSucceeded ||
                string.IsNullOrWhiteSpace(recipientResult.RecipientId))
            {
                return new TransferResult
                {
                    CallSucceeded = false,
                    Status = TransactionStatus.Pending,
                    Reference = reference,
                    Message = recipientResult.Message
                        ?? "Unable to create Flutterwave recipient."
                };
            }

            recipientId = recipientResult.RecipientId;
            bankAccount.FlutterwaveRecipientId = recipientId;
        }

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
                CallSucceeded = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "No response from Flutterwave."
            };
        }

        if (!string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase)
            || response.Data is null)
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = response.Message ?? "Unable to initiate transfer."
            };
        }

        if (!string.Equals(response.Data.Reference, reference, StringComparison.Ordinal))
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Reference mismatch between request and Flutterwave response."
            };
        }

        if (response.Data.Amount != amount.ToDecimal())
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Amount mismatch between request and Flutterwave response."
            };
        }

        return new TransferResult
        {
            CallSucceeded = true,
            Status = FlutterwaveTransferStatusMapper.NormalizeStatus(response.Data.Status),
            Reference = response.Data.Reference!,
            Message = response.Message ?? "Transfer initiated."
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
