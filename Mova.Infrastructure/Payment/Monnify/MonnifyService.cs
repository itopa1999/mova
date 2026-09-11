using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.ExternalAPI;
using Mova.Application.Interfaces.Payment;
using Mova.Infrastructure.ExternalAPI;
using Mova.Infrastructure.Payment.Monnify;

namespace Mova.Infrastructure.Payment;

public sealed class MonnifyService(
    IOptions<MonnifySettings> options,
    IExternalApiClient externalApiClient,
    IOptions<ExternalApiSettings> externalApiSettings)
    : IMonnifyService
{
    private readonly MonnifySettings _settings = options.Value;
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

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Monnify API key is not configured."
            };
        }

        if (string.IsNullOrWhiteSpace(_settings.SecretKey))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Monnify secret key is not configured."
            };
        }

        if (string.IsNullOrWhiteSpace(_settings.ContractCode))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Monnify contract code is not configured."
            };
        }

        // ---------------------------------------------------------
        // 1. Authenticate with Monnify
        // ---------------------------------------------------------

        var credentials =
            $"{_settings.ApiKey}:{_settings.SecretKey}";

        var encodedCredentials =
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(credentials));

        var authUrl =
            $"{_settings.BaseUrl.TrimEnd('/')}/api/v1/auth/login";

        var authHeaders = new Dictionary<string, string>
        {
            ["Authorization"] = $"Basic {encodedCredentials}"
        };

        var authResponse =
            await _externalApiClient.PostAsync<
                object,
                MonnifyAuthResponse>(
                    authUrl,
                    new { },
                    authHeaders,
                    cancellationToken);

        if (authResponse is null)
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "No response from Monnify authentication."
            };
        }

        if (!authResponse.RequestSuccessful ||
            authResponse.ResponseBody is null ||
            string.IsNullOrWhiteSpace(
                authResponse.ResponseBody.AccessToken))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = authResponse.ResponseMessage
                    ?? "Unable to authenticate with Monnify."
            };
        }

        var accessToken =
            authResponse.ResponseBody.AccessToken;

        // ---------------------------------------------------------
        // 2. Initialize Monnify transaction
        // ---------------------------------------------------------

        var transactionUrl =
            $"{_settings.BaseUrl.TrimEnd('/')}" +
            "/api/v1/merchant/transactions/init-transaction";

        var transactionHeaders = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {accessToken}"
        };

        var payload = new MonnifyInitializeRequest
        {
            Amount = amount,
            CustomerEmail = email,
            CustomerName = email,
            PaymentReference = reference,
            PaymentDescription = "MOVA account funding",
            CurrencyCode = "NGN",
            ContractCode = _settings.ContractCode,
            RedirectUrl = _externalApiSettings.PaymentCallbackUrl,

            PaymentMethods =
            [
                "CARD",
                "ACCOUNT_TRANSFER",
                "USSD"
            ]
        };

        var response =
            await _externalApiClient.PostAsync<
                MonnifyInitializeRequest,
                MonnifyInitializeResponse>(
                    transactionUrl,
                    payload,
                    transactionHeaders,
                    cancellationToken);

        if (response is null)
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "No response from Monnify."
            };
        }

        if (!response.RequestSuccessful ||
            response.ResponseBody is null)
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = response.ResponseMessage
                    ?? "Unable to initialize Monnify payment."
            };
        }

        if (string.IsNullOrWhiteSpace(
                response.ResponseBody.CheckoutUrl))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Monnify did not return a checkout URL."
            };
        }

        return new PaymentInitializationResultDto
        {
            Success = true,
            Message = response.ResponseMessage,
            AuthorizationUrl = response.ResponseBody.CheckoutUrl,
        };
    }

    private sealed class MonnifyAuthResponse
    {
        [JsonPropertyName("requestSuccessful")]
        public bool RequestSuccessful { get; set; }

        [JsonPropertyName("responseMessage")]
        public string? ResponseMessage { get; set; }

        [JsonPropertyName("responseCode")]
        public string? ResponseCode { get; set; }

        [JsonPropertyName("responseBody")]
        public MonnifyAuthResponseBody? ResponseBody { get; set; }
    }

    private sealed class MonnifyAuthResponseBody
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }
    }

    private sealed class MonnifyInitializeRequest
    {
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("customerEmail")]
        public string CustomerEmail { get; set; } = string.Empty;

        [JsonPropertyName("customerName")]
        public string CustomerName { get; set; } = string.Empty;

        [JsonPropertyName("paymentReference")]
        public string PaymentReference { get; set; } = string.Empty;

        [JsonPropertyName("paymentDescription")]
        public string PaymentDescription { get; set; } = string.Empty;

        [JsonPropertyName("currencyCode")]
        public string CurrencyCode { get; set; } = "NGN";

        [JsonPropertyName("contractCode")]
        public string ContractCode { get; set; } = string.Empty;

        [JsonPropertyName("redirectUrl")]
        public string RedirectUrl { get; set; } = string.Empty;

        [JsonPropertyName("paymentMethods")]
        public List<string> PaymentMethods { get; set; } = [];
    }

    private sealed class MonnifyInitializeResponse
    {
        [JsonPropertyName("requestSuccessful")]
        public bool RequestSuccessful { get; set; }

        [JsonPropertyName("responseMessage")]
        public string? ResponseMessage { get; set; }

        [JsonPropertyName("responseCode")]
        public string? ResponseCode { get; set; }

        [JsonPropertyName("responseBody")]
        public MonnifyInitializeResponseBody? ResponseBody { get; set; }
    }

    private sealed class MonnifyInitializeResponseBody
    {
        [JsonPropertyName("transactionReference")]
        public string? TransactionReference { get; set; }

        [JsonPropertyName("paymentReference")]
        public string? PaymentReference { get; set; }

        [JsonPropertyName("checkoutUrl")]
        public string? CheckoutUrl { get; set; }
    }
}