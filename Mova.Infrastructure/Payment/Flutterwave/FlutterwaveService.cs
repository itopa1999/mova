using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.ExternalAPI;
using Mova.Application.Interfaces.Payment;
using Mova.Infrastructure.ExternalAPI;

namespace Mova.Infrastructure.Payment.Flutterwave;

public sealed class FlutterwaveService(
    IOptions<FlutterwaveSettings> options,
    IExternalApiClient externalApiClient,
    IOptions<ExternalApiSettings> externalApiSettings)
    : IFlutterwaveService
{
    private readonly FlutterwaveSettings _settings = options.Value;
    private readonly IExternalApiClient _externalApiClient = externalApiClient;
    private readonly ExternalApiSettings _externalApiSettings =
        externalApiSettings.Value;

    public async Task<PaymentInitializationResultDto> InitializePaymentAsync(
        string email,
        decimal amount,
        string reference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Customer email is required."
            };
        }

        if (amount <= 0)
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Payment amount must be greater than zero."
            };
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Payment reference is required."
            };
        }

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/payments";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var payload = new FlutterwaveInitializeRequest
        {
            TxRef = reference,
            Amount = amount,
            Currency = "NGN",
            RedirectUrl = _externalApiSettings.PaymentCallbackUrl,
            Customer = new FlutterwaveCustomer
            {
                Email = email
            }
        };

        var response =
            await _externalApiClient.PostAsync<
                FlutterwaveInitializeRequest,
                FlutterwaveInitializeResponse>(
                    url,
                    payload,
                    headers,
                    cancellationToken);

        if (response is null)
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "No response from Flutterwave."
            };
        }

        if (response.Status != "success" || response.Data is null)
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = response.Message
                    ?? "Unable to initialize Flutterwave payment."
            };
        }

        return new PaymentInitializationResultDto
        {
            Success = true,
            Message = response.Message,
            AuthorizationUrl = response.Data.Link,
        };
    }

    public Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature)
    {
        if (string.IsNullOrWhiteSpace(_settings.SecretKey)
            || string.IsNullOrWhiteSpace(signature))
        {
            return Task.FromResult(false);
        }

        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(_settings.SecretKey));

        var expectedSignature = Convert.ToHexString(
            hmac.ComputeHash(rawBody))
            .ToLowerInvariant();

        var expectedBytes =
            Encoding.UTF8.GetBytes(expectedSignature);

        var actualBytes =
            Encoding.UTF8.GetBytes(signature.Trim());

        return Task.FromResult(
            CryptographicOperations.FixedTimeEquals(
                expectedBytes,
                actualBytes));
    }

    private sealed class FlutterwaveInitializeRequest
    {
        [JsonPropertyName("tx_ref")]
        public string TxRef { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";

        [JsonPropertyName("redirect_url")]
        public string RedirectUrl { get; set; } = string.Empty;

        [JsonPropertyName("customer")]
        public FlutterwaveCustomer Customer { get; set; } = new();
    }

    private sealed class FlutterwaveCustomer
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    private sealed class FlutterwaveInitializeResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public FlutterwaveInitializeData? Data { get; set; }
    }

    private sealed class FlutterwaveInitializeData
    {
        [JsonPropertyName("link")]
        public string Link { get; set; } = string.Empty;
    }
}