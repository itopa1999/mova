using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
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
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Reference is required."
            };
        }

        if (amount.MinorUnits <= 0)
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Amount must be greater than zero."
            };
        }

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var recipientCode = bankAccount.PaystackRecipientCode;

        if (string.IsNullOrWhiteSpace(recipientCode))
        {
            var recipientResult = await CreateRecipientAsync(
                bankAccount, headers, cancellationToken);

            if (!recipientResult.CallSucceeded ||
                string.IsNullOrWhiteSpace(recipientResult.RecipientCode))
            {
                return new TransferResult
                {
                    CallSucceeded = false,
                    Found = false,
                    Status = TransactionStatus.Pending,
                    Reference = reference,
                    Message = recipientResult.Message ?? "Unable to create transfer recipient."
                };
            }

            recipientCode = recipientResult.RecipientCode;
            bankAccount.PaystackRecipientCode = recipientCode;
        }

        var transferUrl =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfer";

        var payload = new PaystackTransferRequest
        {
            Source = "balance",
            Amount = (long)amount.MinorUnits,
            Recipient = recipientCode,
            Reason = $"Payout {reference}",
            Reference = reference
        };

        var response =
            await _externalApiClient.PostAsync<
                PaystackTransferRequest,
                PaystackPaymentInitiatorTransferResponse>(
                transferUrl,
                payload,
                headers,
                cancellationToken);

        if (response is null)
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "No response from Paystack."
            };
        }

        if (!response.Status || response.Data is null)
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Found = false,
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
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Reference mismatch between request and Paystack response."
            };
        }

        if (response.Data.Amount != (long)amount.MinorUnits)
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Amount mismatch between request and Paystack response."
            };
        }

        return new TransferResult
        {
            CallSucceeded = true,
            Found = true,
            Status = PaystackTransferStatusMapper.NormalizeStatus(response.Data.Status),
            Reference = response.Data.Reference!,
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

    private sealed class PaystackPaymentInitiatorTransferResponse
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