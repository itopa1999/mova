using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.ExternalAPI;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment.Paystack;

public sealed class PaystackService(
    IOptions<PaystackSettings> options,
    IExternalApiClient externalApiClient) : IPaystackService
{
    private readonly PaystackSettings _settings = options.Value;
    private readonly IExternalApiClient _externalApiClient = externalApiClient;

    public async Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature)
    {
        var secretKeyBytes =
            Encoding.UTF8.GetBytes(_settings.SecretKey);

        using var hmac = new HMACSHA512(secretKeyBytes);

        var hashBytes = hmac.ComputeHash(rawBody);

        var expectedSignature =
            Convert.ToHexString(hashBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signature));
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
}