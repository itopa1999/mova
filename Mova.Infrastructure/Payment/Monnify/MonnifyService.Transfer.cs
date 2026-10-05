using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService
{
    public async Task<TransferResult> TransferAsync(BankAccount account, Money amount, string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(account.AccountName) || string.IsNullOrWhiteSpace(_settings.SourceAccountNumber))
            return Failed(reference, "Destination account name or Monnify source account is missing.");
        var token = await AuthenticateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return Failed(reference, "Unable to authenticate with Monnify.");
        var request = new MonnifyTransferRequest { Amount = amount.ToDecimal(), Reference = reference, Narration = $"Payout {reference}", DestinationBankCode = account.BankCode, DestinationAccountNumber = account.AccountNumber, DestinationAccountName = account.AccountName, Currency = string.IsNullOrWhiteSpace(account.Currency) ? "NGN" : account.Currency!, SourceAccountNumber = _settings.SourceAccountNumber };
        var response = await _externalApiClient.PostAsync<MonnifyTransferRequest, MonnifyTransferResponse>($"{_settings.BaseUrl.TrimEnd('/')}/api/v2/disbursements/single", request, Bearer(token), cancellationToken);
        return ToTransferResult(response, reference, "Unable to initiate Monnify transfer.");
    }

    public async Task<TransferResult> VerifyTransferAsync(string reference, CancellationToken cancellationToken = default)
    {
        var token = await AuthenticateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return Failed(reference, "Unable to authenticate with Monnify.");
        var response = await _externalApiClient.GetAsync<MonnifyTransferResponse>($"{_settings.BaseUrl.TrimEnd('/')}/api/v2/disbursements/single/summary?reference={Uri.EscapeDataString(reference)}", Bearer(token), cancellationToken);
        return ToTransferResult(response, reference, "Unable to verify Monnify transfer.");
    }

    private static Dictionary<string, string> Bearer(string token) => new() { ["Authorization"] = $"Bearer {token}" };
    private static TransferResult Failed(string reference, string message) => new() { IsSuccessful = false, Status = "failed", Reference = reference, Message = message };
    private static TransferResult ToTransferResult(MonnifyTransferResponse? response, string reference, string fallback) => response is { RequestSuccessful: true, ResponseBody: not null }
        ? new() { IsSuccessful = true, Status = NormalizeTransferStatus(response.ResponseBody.Status), Reference = response.ResponseBody.Reference ?? reference, Message = response.ResponseMessage ?? "Transfer processed." }
        : Failed(reference, response?.ResponseMessage ?? fallback);
    private static string NormalizeTransferStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "SUCCESS" or "COMPLETED" => "success", "FAILED" or "REJECTED" => "failed", "REVERSED" or "REFUNDED" => "reversed", _ => "pending"
    };

    private sealed class MonnifyTransferRequest
    {
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
        [JsonPropertyName("narration")] public string Narration { get; set; } = string.Empty;
        [JsonPropertyName("destinationBankCode")] public string DestinationBankCode { get; set; } = string.Empty;
        [JsonPropertyName("destinationAccountNumber")] public string DestinationAccountNumber { get; set; } = string.Empty;
        [JsonPropertyName("destinationAccountName")] public string DestinationAccountName { get; set; } = string.Empty;
        [JsonPropertyName("currency")] public string Currency { get; set; } = "NGN";
        [JsonPropertyName("sourceAccountNumber")] public string SourceAccountNumber { get; set; } = string.Empty;
    }
    private sealed class MonnifyTransferResponse
    {
        [JsonPropertyName("requestSuccessful")] public bool RequestSuccessful { get; set; }
        [JsonPropertyName("responseMessage")] public string? ResponseMessage { get; set; }
        [JsonPropertyName("responseBody")] public MonnifyTransferResponseBody? ResponseBody { get; set; }
    }
    private sealed class MonnifyTransferResponseBody
    {
        [JsonPropertyName("reference")] public string? Reference { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }
}
