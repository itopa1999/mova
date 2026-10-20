using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService
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
                CallSucceeded = false,
                Message = "A valid customer email is required."
            };
        }

        if (amount <= 0)
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = "Amount must be greater than zero."
            };
        }

        if (string.IsNullOrWhiteSpace(reference) ||
            reference.Any(c => !(c is (>= 'a' and <= 'z')
                                    or (>= 'A' and <= 'Z')
                                    or (>= '0' and <= '9')
                                    or '-' or '.' or '=' or '_')))
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = "Reference contains invalid characters. " +
                          "Only letters, digits, '-', '.', and '=' are allowed."
            };
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
            string.IsNullOrWhiteSpace(_settings.SecretKey) ||
            string.IsNullOrWhiteSpace(_settings.ContractCode))
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = "Monnify credentials are not configured."
            };
        }

        var token = await AuthenticateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = "Unable to authenticate with Monnify."
            };
        }

        var request = new MonnifyInitializeRequest
        {
            Amount = amount,
            CustomerEmail = email,
            CustomerName = null,
            PaymentReference = reference,
            PaymentDescription = "MOVA account funding",
            CurrencyCode = "NGN",
            ContractCode = _settings.ContractCode,
            RedirectUrl = _externalApiSettings.PaymentCallbackUrl,
            PaymentMethods = ["CARD", "ACCOUNT_TRANSFER", "USSD"]
        };

        var response =
            await _externalApiClient.PostAsync<MonnifyInitializeRequest, MonnifyInitializeResponse>(
                $"{_settings.BaseUrl.TrimEnd('/')}/api/v1/merchant/transactions/init-transaction",
                request,
                new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
                cancellationToken);

        if (response is null)
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = "No response from Monnify."
            };
        }

        if (!response.RequestSuccessful ||
            response.ResponseBody is null ||
            string.IsNullOrWhiteSpace(response.ResponseBody.CheckoutUrl))
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = response.ResponseMessage ?? "Unable to initialize Monnify payment."
            };
        }

        if (!string.Equals(
                response.ResponseBody.PaymentReference,
                reference,
                StringComparison.Ordinal))
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = "Payment reference mismatch between request and Monnify response."
            };
        }

        if (response.ResponseBody.Amount != amount)
        {
            return new PaymentInitializationResultDto
            {
                CallSucceeded = false,
                Message = "Amount mismatch between request and Monnify response."
            };
        }

        return new PaymentInitializationResultDto
        {
            CallSucceeded = true,
            Message = response.ResponseMessage ?? "Payment initialized.",
            AuthorizationUrl = response.ResponseBody.CheckoutUrl,
            TransactionReference = response.ResponseBody.TransactionReference,
            PaymentReference = response.ResponseBody.PaymentReference,
        };
    }

    private sealed class MonnifyInitializeRequest
    {
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("customerEmail")]
        public string CustomerEmail { get; set; } = string.Empty;

        [JsonPropertyName("customerName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CustomerName { get; set; }

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

        [JsonPropertyName("responseBody")]
        public MonnifyInitializeResponseBody? ResponseBody { get; set; }
    }

    private sealed class MonnifyInitializeResponseBody
    {
        [JsonPropertyName("checkoutUrl")]
        public string? CheckoutUrl { get; set; }

        [JsonPropertyName("transactionReference")]
        public string? TransactionReference { get; set; }

        [JsonPropertyName("paymentReference")]
        public string? PaymentReference { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("currencyCode")]
        public string? CurrencyCode { get; set; }
    }
}