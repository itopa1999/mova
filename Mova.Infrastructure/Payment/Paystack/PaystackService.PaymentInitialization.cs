using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    public async Task<PaymentInitializationResultDto> InitializePaymentAsync(
        string email,
        decimal amount,
        string reference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "A valid email address is required."
            };
        }

        if (amount <= 0)
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Amount must be greater than zero."
            };
        }

        if (string.IsNullOrWhiteSpace(reference) ||
            reference.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '.' && c != '='))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Reference contains invalid characters."
            };
        }

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

        if (!response.Status || response.Data is null ||
            string.IsNullOrWhiteSpace(response.Data.AuthorizationUrl))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = response.Message ?? "Unable to initialize Paystack payment."
            };
        }

        if (!string.Equals(response.Data.Reference, reference, StringComparison.Ordinal))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Reference mismatch between request and Paystack response."
            };
        }

        return new PaymentInitializationResultDto
        {
            Success = true,
            AuthorizationUrl = response.Data.AuthorizationUrl,
            AccessCode = response.Data.AccessCode,
            Reference = response.Data.Reference
        };
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