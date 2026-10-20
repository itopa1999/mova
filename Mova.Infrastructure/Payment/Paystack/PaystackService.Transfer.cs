using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
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
        // Paystack requires a recipient_code for every transfer.
        var recipientCode = bankAccount.PaystackRecipientCode;

        if (string.IsNullOrWhiteSpace(recipientCode))
        {
            var recipientResult = await CreateRecipientAsync(
                bankAccount, headers, cancellationToken);

            if (!recipientResult.IsSuccessful)
                return recipientResult;

            recipientCode = recipientResult.Reference;

            // Persist the code so we only create the recipient once.
            // The caller (job) is responsible for saving the entity.
            bankAccount.PaystackRecipientCode = recipientCode;
        }

        // Step 2 — initiate the transfer.
        var transferUrl =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfer";

        var payload = new PaystackTransferRequest
        {
            Source = "balance",
            Amount = (long)amount.MinorUnits,
            Recipient = recipientCode!,
            Reason = $"Payout {reference}",
            Reference = reference
        };

        var response =
            await _externalApiClient.PostAsync<PaystackTransferRequest, PaystackPaymentInintiatorTransferResponse>(
                transferUrl,
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

    private sealed class PaystackTransferRequest
    {
        [JsonPropertyName("source")]
        public string Source { get; set; } = "balance";

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("recipient")]
        public string Recipient { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;
    }

    private sealed class PaystackPaymentInintiatorTransferResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackTransferData? Data { get; set; }
    }

    private sealed class PaystackTransferData
    {
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("transfer_code")]
        public string? TransferCode { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("recipient")]
        public string? Recipient { get; set; }
    }
}
