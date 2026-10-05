using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Infrastructure.Payment.Monnify;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService
{
    public async Task<PaymentVerificationResult> VerifyPaymentAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference)) return new() { Reference = reference, Status = "pending", Message = "Reference is required." };
        var token = await AuthenticateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return new() { Reference = reference, Status = "pending", Message = "Unable to authenticate with Monnify." };

        var response = await _externalApiClient.GetAsync<MonnifyVerifyPaymentResponse>(
            $"{_settings.BaseUrl.TrimEnd('/')}/api/v2/merchant/transactions/query?paymentReference={Uri.EscapeDataString(reference)}",
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }, cancellationToken);
        if (response is null) return new() { Reference = reference, Status = "pending", Message = "No response from Monnify." };
        if (!response.RequestSuccessful && response.ResponseCode == "99") return new() { IsSuccessful = true, Found = false, Reference = reference, Status = "not_found", Message = response.ResponseMessage ?? "Transaction not found." };
        if (response is not { RequestSuccessful: true, ResponseBody: not null }) return new() { Reference = reference, Status = "pending", Message = response.ResponseMessage ?? "Unable to verify payment." };
        if (!string.Equals(response.ResponseBody.PaymentReference, reference, StringComparison.Ordinal)) return new() { Reference = reference, Status = "pending", Message = "Monnify returned a mismatched payment reference." };

        var status = MonnifyTransactionStatusMapper.ToTransactionStatus(response.ResponseBody.PaymentStatus);
        return new() { IsSuccessful = true, Found = true, Reference = reference, LocalStatus = status, Status = MonnifyTransactionStatusMapper.ToLegacyResultStatus(status), Message = response.ResponseMessage ?? string.Empty };
    }

    private sealed class MonnifyVerifyPaymentResponse
    {
        [JsonPropertyName("requestSuccessful")] public bool RequestSuccessful { get; set; }
        [JsonPropertyName("responseMessage")] public string? ResponseMessage { get; set; }
        [JsonPropertyName("responseCode")] public string? ResponseCode { get; set; }
        [JsonPropertyName("responseBody")] public MonnifyVerifyPaymentBody? ResponseBody { get; set; }
    }
    private sealed class MonnifyVerifyPaymentBody
    {
        [JsonPropertyName("paymentReference")] public string? PaymentReference { get; set; }
        [JsonPropertyName("paymentStatus")] public string? PaymentStatus { get; set; }
    }
}
