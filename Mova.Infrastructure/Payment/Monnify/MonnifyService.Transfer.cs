using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Payment.Monnify;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService
{
    public async Task<TransferResult> TransferAsync(
        BankAccount account,
        Money amount,
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Failed(reference, "Reference is required.");
        }

        if (amount.ToDecimal() <= 0)
        {
            return Failed(reference, "Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(account.AccountName) ||
            string.IsNullOrWhiteSpace(account.AccountNumber) ||
            string.IsNullOrWhiteSpace(account.BankCode))
        {
            return Failed(reference,
                "Destination account name, number, and bank code are required. " +
                "Run Name Enquiry first to obtain the verified account name.");
        }

        if (string.IsNullOrWhiteSpace(_settings.SourceAccountNumber))
        {
            return Failed(reference, "Monnify source account is not configured.");
        }

        var token = await AuthenticateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return Failed(reference, "Unable to authenticate with Monnify.");
        }

        var request = new MonnifyTransferRequest
        {
            Amount = amount.ToDecimal(),
            Reference = reference,
            Narration = $"Payout {reference}",
            DestinationBankCode = account.BankCode,
            DestinationAccountNumber = account.AccountNumber,
            DestinationAccountName = account.AccountName,
            Currency = string.IsNullOrWhiteSpace(account.Currency)
                ? "NGN"
                : account.Currency!,
            SourceAccountNumber = _settings.SourceAccountNumber
        };

        var response =
            await _externalApiClient.PostAsync<MonnifyTransferRequest, MonnifyTransferResponse>(
                $"{_settings.BaseUrl.TrimEnd('/')}/api/v2/disbursements/single",
                request,
                Bearer(token),
                cancellationToken);

        return ToTransferResult(
            response,
            reference,
            amount.ToDecimal(),
            "Unable to initiate Monnify transfer.");
    }

    private static Dictionary<string, string> Bearer(string token)
        => new() { ["Authorization"] = $"Bearer {token}" };

    private static TransferResult Failed(string reference, string message)
        => new()
        {
            CallSucceeded = false,
            Found = false,
            Status = TransactionStatus.Pending,
            Reference = reference,
            Message = message
        };

    private static TransferResult ToTransferResult(
        MonnifyTransferResponse? response,
        string reference,
        decimal? expectedAmount,
        string fallback)
    {
        if (response is null)
        {
            return Failed(reference, "No response from Monnify.");
        }

        if (!response.RequestSuccessful || response.ResponseBody is null)
        {
            // Could be "not found" or a transient error — we cannot tell
            // the two apart safely, so treat as unknown and retry.
            return Failed(reference, response.ResponseMessage ?? fallback);
        }

        if (!string.Equals(
                response.ResponseBody.Reference,
                reference,
                StringComparison.Ordinal))
        {
            return Failed(reference,
                "Reference mismatch between request and Monnify response.");
        }

        if (expectedAmount is > 0 &&
            response.ResponseBody.Amount != expectedAmount.Value)
        {
            return Failed(reference,
                $"Amount mismatch: requested {expectedAmount.Value:N2}, " +
                $"Monnify recorded {response.ResponseBody.Amount:N2}.");
        }

        var status =
            MonnifyTransferStatusMapper.ToTransactionStatus(response.ResponseBody.Status);

        var message = status switch
        {
            TransactionStatus.Completed =>
                response.ResponseBody.TransactionDescription
                    ?? response.ResponseMessage
                    ?? "Transfer completed.",
            TransactionStatus.Failed =>
                response.ResponseBody.TransactionDescription
                    ?? response.ResponseMessage
                    ?? "Transfer failed.",
            TransactionStatus.Reversed =>
                "Transfer was reversed.",
            _ =>
                response.ResponseMessage ?? "Transfer is pending."
        };

        return new TransferResult
        {
            CallSucceeded = true,
            Found = true,
            Status = status,
            Reference = response.ResponseBody.Reference!,
            Message = message
        };
    }

    private sealed class MonnifyTransferRequest
    {
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;

        [JsonPropertyName("narration")]
        public string Narration { get; set; } = string.Empty;

        [JsonPropertyName("destinationBankCode")]
        public string DestinationBankCode { get; set; } = string.Empty;

        [JsonPropertyName("destinationAccountNumber")]
        public string DestinationAccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("destinationAccountName")]
        public string DestinationAccountName { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";

        [JsonPropertyName("sourceAccountNumber")]
        public string SourceAccountNumber { get; set; } = string.Empty;
    }

    private sealed class MonnifyTransferResponse
    {
        [JsonPropertyName("requestSuccessful")]
        public bool RequestSuccessful { get; set; }

        [JsonPropertyName("responseMessage")]
        public string? ResponseMessage { get; set; }

        [JsonPropertyName("responseCode")]
        public string? ResponseCode { get; set; }

        [JsonPropertyName("responseBody")]
        public MonnifyTransferResponseBody? ResponseBody { get; set; }
    }

    private sealed class MonnifyTransferResponseBody
    {
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("transactionReference")]
        public string? TransactionReference { get; set; }

        [JsonPropertyName("transactionDescription")]
        public string? TransactionDescription { get; set; }

        [JsonPropertyName("twoFaEnabled")]
        public bool TwoFaEnabled { get; set; }

        [JsonPropertyName("destinationAccountNumber")]
        public string? DestinationAccountNumber { get; set; }

        [JsonPropertyName("destinationAccountName")]
        public string? DestinationAccountName { get; set; }

        [JsonPropertyName("destinationBankCode")]
        public string? DestinationBankCode { get; set; }

        [JsonPropertyName("destinationBankName")]
        public string? DestinationBankName { get; set; }

        [JsonPropertyName("createdOn")]
        public string? CreatedOn { get; set; }
    }
}