using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.ExternalAPI;
using Mova.Application.Interfaces.Payment;
using Mova.Infrastructure.ExternalAPI;

namespace Mova.Infrastructure.Payment.Paystack;

public sealed class PaystackService(
    IOptions<PaystackSettings> options,
    IExternalApiClient externalApiClient,
    IOptions<ExternalApiSettings> externalApiSettings) : IPaystackService
{
    private readonly PaystackSettings _settings = options.Value;
    private readonly IExternalApiClient _externalApiClient = externalApiClient;
    private readonly ExternalApiSettings _externalApiSettings = externalApiSettings.Value;

    public Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature)
    {
        var secretKeyBytes =
            Encoding.UTF8.GetBytes(_settings.SecretKey);

        using var hmac = new HMACSHA512(secretKeyBytes);

        var hashBytes = hmac.ComputeHash(rawBody);

        var expectedSignature =
            Convert.ToHexString(hashBytes).ToLowerInvariant();

        var result = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signature));

        return Task.FromResult(result);
    }

    public async Task<ResolveBankAccountResponse?> ResolveBankAccountAsync(
        string accountNumber,
        string bankCode,
        CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);

        query["account_number"] = accountNumber;
        query["bank_code"] = bankCode;

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/bank/resolve?{query}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var response =
            await _externalApiClient.GetAsync<PaystackResolveAccountResponse>(
                url,
                headers,
                cancellationToken);

        if (response is null || !response.Status || response.Data is null)
            return null;

        return new ResolveBankAccountResponse
        {
            AccountNumber = response.Data.AccountNumber,
            AccountName = response.Data.AccountName
        };
    }

    public async Task<PaymentInitializationResultDto> InitializePaymentAsync(
        string email,
        decimal amount,
        string reference,
        CancellationToken cancellationToken)
    {
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transaction/initialize";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var payload = new PaystackInitializeRequest
        {
            Email = email,
            Amount = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero),
            Reference = reference,
            CallbackUrl = _externalApiSettings.PaymentCallbackUrl
        };

        var response =
            await _externalApiClient.PostAsync<PaystackInitializeRequest, PaystackInitializeResponse>(
                url,
                payload,
                headers,
                cancellationToken);

        if (response is null)
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "No response from Paystack."
            };
        }

        if (!response.Status || response.Data is null)
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = response.Message ?? "Unable to initialize Paystack payment."
            };
        }

        return new PaymentInitializationResultDto
        {
            Success = true,
            AuthorizationUrl = response.Data.AuthorizationUrl,
        };
    }

    private sealed class PaystackResolveAccountResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackAccountData? Data { get; set; }
    }

    private sealed class PaystackAccountData
    {
        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("account_name")]
        public string AccountName { get; set; } = string.Empty;

        [JsonPropertyName("bank_id")]
        public int? BankId { get; set; }
    }

    private sealed class PaystackInitializeRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;

        [JsonPropertyName("callback_url")]
        public string? CallbackUrl { get; set; }
    }

    private sealed class PaystackInitializeResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackInitializeData? Data { get; set; }
    }

    private sealed class PaystackInitializeData
    {
        [JsonPropertyName("authorization_url")]
        public string AuthorizationUrl { get; set; } = string.Empty;

        [JsonPropertyName("access_code")]
        public string AccessCode { get; set; } = string.Empty;

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;
    }
}