using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService
{
    public async Task<PaymentInitializationResultDto> InitializePaymentAsync(string email, decimal amount, string reference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || amount <= 0 || string.IsNullOrWhiteSpace(reference))
            return new() { Success = false, Message = "Email, a positive amount, and payment reference are required." };
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.SecretKey) || string.IsNullOrWhiteSpace(_settings.ContractCode))
            return new() { Success = false, Message = "Monnify credentials are not configured." };

        var token = await AuthenticateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return new() { Success = false, Message = "Unable to authenticate with Monnify." };

        var request = new MonnifyInitializeRequest
        {
            Amount = amount, CustomerEmail = email, CustomerName = email, PaymentReference = reference,
            PaymentDescription = "MOVA account funding", CurrencyCode = "NGN", ContractCode = _settings.ContractCode,
            RedirectUrl = _externalApiSettings.PaymentCallbackUrl, PaymentMethods = ["CARD", "ACCOUNT_TRANSFER", "USSD"]
        };
        var response = await _externalApiClient.PostAsync<MonnifyInitializeRequest, MonnifyInitializeResponse>(
            $"{_settings.BaseUrl.TrimEnd('/')}/api/v1/merchant/transactions/init-transaction", request,
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }, cancellationToken);

        if (response is not { RequestSuccessful: true, ResponseBody.CheckoutUrl: { Length: > 0 } })
            return new() { Success = false, Message = response?.ResponseMessage ?? "Unable to initialize Monnify payment." };
        return new() { Success = true, Message = response.ResponseMessage, AuthorizationUrl = response.ResponseBody.CheckoutUrl };
    }

    private sealed class MonnifyInitializeRequest
    {
        [JsonPropertyName("amount")] public decimal Amount { get; set; }
        [JsonPropertyName("customerEmail")] public string CustomerEmail { get; set; } = string.Empty;
        [JsonPropertyName("customerName")] public string CustomerName { get; set; } = string.Empty;
        [JsonPropertyName("paymentReference")] public string PaymentReference { get; set; } = string.Empty;
        [JsonPropertyName("paymentDescription")] public string PaymentDescription { get; set; } = string.Empty;
        [JsonPropertyName("currencyCode")] public string CurrencyCode { get; set; } = "NGN";
        [JsonPropertyName("contractCode")] public string ContractCode { get; set; } = string.Empty;
        [JsonPropertyName("redirectUrl")] public string RedirectUrl { get; set; } = string.Empty;
        [JsonPropertyName("paymentMethods")] public List<string> PaymentMethods { get; set; } = [];
    }
    private sealed class MonnifyInitializeResponse
    {
        [JsonPropertyName("requestSuccessful")] public bool RequestSuccessful { get; set; }
        [JsonPropertyName("responseMessage")] public string? ResponseMessage { get; set; }
        [JsonPropertyName("responseBody")] public MonnifyInitializeResponseBody? ResponseBody { get; set; }
    }
    private sealed class MonnifyInitializeResponseBody
    {
        [JsonPropertyName("checkoutUrl")] public string? CheckoutUrl { get; set; }
    }
}
