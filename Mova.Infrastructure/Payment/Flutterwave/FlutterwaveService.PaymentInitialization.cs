using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class FlutterwaveService
{
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
                CallSucceeded = false,
                Message = "Customer email is required."
            };
        }

        if (amount <= 0)
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = "Payment amount must be greater than zero."
            };
        }

        if (string.IsNullOrWhiteSpace(reference))
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
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
                CallSucceeded = false,
                Message = "No response from Flutterwave."
            };
        }

        if (!string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase)
            || response.Data is null
            || string.IsNullOrWhiteSpace(response.Data.Link))
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = response.Message ?? "Unable to initialize Flutterwave payment."
            };
        }

        return new PaymentInitializationResultDto
        {
            CallSucceeded = true,
            Message = response.Message ?? "Payment initialized.",
            AuthorizationUrl = response.Data.Link,
            Reference = reference
        };
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

    private sealed class FlutterwaveCustomer
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }
}