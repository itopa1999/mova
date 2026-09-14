using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.ExternalAPI;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;
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

        var accessToken = await AuthenticateAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new PaymentInitializationResultDto
            {
                Success = false,
                Message = "Unable to authenticate with Monnify."
            };
        }

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

    public async Task<TransferResult> TransferAsync(
        BankAccount bankAccount,
        Money amount,
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bankAccount.AccountName))
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = "Account name is required for Monnify transfers."
            };
        }

        if (string.IsNullOrWhiteSpace(_settings.SourceAccountNumber))
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = "Monnify source account number is not configured."
            };
        }

        var accessToken = await AuthenticateAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = "Unable to authenticate with Monnify."
            };
        }

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}" +
            "/api/v2/disbursements/single";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {accessToken}"
        };

        var payload = new MonnifyTransferRequest
        {
            Amount = amount.ToDecimal(),
            Reference = reference,
            Narration = $"Payout {reference}",
            DestinationBankCode = bankAccount.BankCode,
            DestinationAccountNumber = bankAccount.AccountNumber,
            DestinationAccountName = bankAccount.AccountName,
            Currency = string.IsNullOrWhiteSpace(bankAccount.Currency)
                ? "NGN"
                : bankAccount.Currency!,
            SourceAccountNumber = _settings.SourceAccountNumber
        };

        var response =
            await _externalApiClient.PostAsync<
                MonnifyTransferRequest,
                MonnifyTransferResponse>(
                    url,
                    payload,
                    headers,
                    cancellationToken);

        if (response is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = "No response from Monnify."
            };
        }

        if (!response.RequestSuccessful || response.ResponseBody is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = response.ResponseMessage
                    ?? "Unable to initiate Monnify transfer."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = NormalizeStatus(response.ResponseBody.Status),
            Reference = response.ResponseBody.Reference ?? reference,
            Message = response.ResponseMessage ?? "Transfer initiated."
        };
    }

    public async Task<TransferResult> VerifyTransferAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await AuthenticateAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = "Unable to authenticate with Monnify."
            };
        }

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}" +
            "/api/v2/disbursements/single/summary" +
            $"?reference={Uri.EscapeDataString(reference)}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {accessToken}"
        };

        var response =
            await _externalApiClient.GetAsync<MonnifyTransferResponse>(
                url,
                headers,
                cancellationToken);

        if (response is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = "No response from Monnify."
            };
        }

        if (!response.RequestSuccessful || response.ResponseBody is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = response.ResponseMessage
                    ?? "Unable to verify Monnify transfer."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = NormalizeStatus(response.ResponseBody.Status),
            Reference = response.ResponseBody.Reference ?? reference,
            Message = response.ResponseMessage ?? "Transfer verified."
        };
    }

    private async Task<string?> AuthenticateAsync(
        CancellationToken cancellationToken)
    {
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

        if (authResponse is null
            || !authResponse.RequestSuccessful
            || authResponse.ResponseBody is null
            || string.IsNullOrWhiteSpace(
                authResponse.ResponseBody.AccessToken))
        {
            return null;
        }

        return authResponse.ResponseBody.AccessToken;
    }

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "pending";

        return status.ToUpperInvariant() switch
        {
            "SUCCESS" => "success",
            "COMPLETED" => "success",
            "FAILED" => "failed",
            "REJECTED" => "failed",
            "REVERSED" => "reversed",
            "REFUNDED" => "reversed",
            "PENDING" => "pending",
            "IN_PROGRESS" => "pending",
            "PROCESSING" => "pending",
            "QUEUED" => "pending",
            "AWAITING_OTP" => "pending",
            _ => "pending"
        };
    }

    public async Task<PaymentVerificationResult> VerifyPaymentAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = false,
                Found = false,
                Status = "pending",
                Reference = reference,
                Message = "Reference is required."
            };
        }

        var accessToken = await AuthenticateAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = false,
                Found = false,
                Status = "pending",
                Reference = reference,
                Message = "Unable to authenticate with Monnify."
            };
        }

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}" +
            $"/api/v2/transactions/{Uri.EscapeDataString(reference)}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {accessToken}"
        };

        var response =
            await _externalApiClient.GetAsync<MonnifyVerifyPaymentResponse>(
                url,
                headers,
                cancellationToken);

        if (response is null)
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = false,
                Found = false,
                Status = "pending",
                Reference = reference,
                Message = "No response from Monnify."
            };
        }

        // Monnify returns responseCode "99" (or a message containing
        // "not found") when the reference doesn't exist.
        if (!response.RequestSuccessful
            && response.ResponseCode == "99")
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = true,
                Found = false,
                Status = "not_found",
                Reference = reference,
                Message = response.ResponseMessage ?? "Transaction not found."
            };
        }

        if (!response.RequestSuccessful || response.ResponseBody is null)
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = false,
                Found = false,
                Status = "pending",
                Reference = reference,
                Message = response.ResponseMessage ?? "Unable to verify payment."
            };
        }

        return new PaymentVerificationResult
        {
            IsSuccessful = true,
            Found = true,
            Status = NormalizePaymentStatus(response.ResponseBody.PaymentStatus),
            Reference = reference,
            Message = response.ResponseMessage,
        };
    }

    private static string NormalizePaymentStatus(string? status)
{
    if (string.IsNullOrWhiteSpace(status))
        return "pending";

    return status.ToUpperInvariant() switch
    {
        "PAID" => "success",
        "OVERPAID" => "success",
        "FAILED" => "failed",
        "CANCELLED" => "failed",
        "EXPIRED" => "failed",
        "PARTIALLY_PAID" => "pending",
        "PENDING" => "pending",
        "IN_PROGRESS" => "pending",
        "PROCESSING" => "pending",
        _ => "pending"
    };
}

    // ---------------------------------------------------------------------
    // Response and request DTOs
    // ---------------------------------------------------------------------

    private sealed class MonnifyVerifyPaymentResponse
    {
        [JsonPropertyName("requestSuccessful")]
        public bool RequestSuccessful { get; set; }

        [JsonPropertyName("responseMessage")]
        public string? ResponseMessage { get; set; }

        [JsonPropertyName("responseCode")]
        public string? ResponseCode { get; set; }

        [JsonPropertyName("responseBody")]
        public MonnifyVerifyPaymentBody? ResponseBody { get; set; }
    }

    private sealed class MonnifyVerifyPaymentBody
    {
        [JsonPropertyName("transactionReference")]
        public string? TransactionReference { get; set; }

        [JsonPropertyName("paymentReference")]
        public string? PaymentReference { get; set; }

        [JsonPropertyName("amountPaid")]
        public decimal? AmountPaid { get; set; }

        [JsonPropertyName("totalPayable")]
        public decimal? TotalPayable { get; set; }

        [JsonPropertyName("settlementAmount")]
        public decimal? SettlementAmount { get; set; }

        [JsonPropertyName("paidOn")]
        public string? PaidOn { get; set; }

        [JsonPropertyName("paymentStatus")]
        public string? PaymentStatus { get; set; }

        [JsonPropertyName("paymentDescription")]
        public string? PaymentDescription { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("paymentMethod")]
        public string? PaymentMethod { get; set; }
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

    private sealed class MonnifyTransferRequest
    {
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;

        [JsonPropertyName("narration")]
        public string Narration { get; set; } = string.Empty;

        [JsonPropertyName("destinationBankCode")]
        public string DestinationBankCode { get; set; } = string.Empty;

        [JsonPropertyName("destinationAccountNumber")]
        public string DestinationAccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("destinationAccountName")]
        public string DestinationAccountName { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";

        [JsonPropertyName("sourceAccountNumber")]
        public string SourceAccountNumber { get; set; } = string.Empty;
    }

    private sealed class MonnifyTransferResponse
    {
        [JsonPropertyName("requestSuccessful")]
        public bool RequestSuccessful { get; set; }

        [JsonPropertyName("responseMessage")]
        public string? ResponseMessage { get; set; }

        [JsonPropertyName("responseCode")]
        public string? ResponseCode { get; set; }

        [JsonPropertyName("responseBody")]
        public MonnifyTransferResponseBody? ResponseBody { get; set; }
    }

    private sealed class MonnifyTransferResponseBody
    {
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public decimal? Amount { get; set; }

        [JsonPropertyName("destinationAccountName")]
        public string? DestinationAccountName { get; set; }

        [JsonPropertyName("destinationAccountNumber")]
        public string? DestinationAccountNumber { get; set; }

        [JsonPropertyName("destinationBankCode")]
        public string? DestinationBankCode { get; set; }
    }
}