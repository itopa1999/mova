using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.ExternalAPI;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;
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

    public async Task<TransferResult> TransferAsync(
        BankAccount bankAccount,
        Money amount,
        string reference,
        CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        // Step 1 — create a recipient if we don't already have one.
        // Paystack requires a recipient_code for every transfer.
        var recipientCode = bankAccount.PaystackRecipientCode;

        if (string.IsNullOrWhiteSpace(recipientCode))
        {
            var recipientResult = await CreateRecipientAsync(
                bankAccount, headers, cancellationToken);

            if (!recipientResult.IsSuccessful)
                return recipientResult;

            recipientCode = recipientResult.Reference;

            // Persist the code so we only create the recipient once.
            // The caller (job) is responsible for saving the entity.
            bankAccount.PaystackRecipientCode = recipientCode;
        }

        // Step 2 — initiate the transfer.
        var transferUrl =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfer";

        var payload = new PaystackTransferRequest
        {
            Source = "balance",
            Amount = (long)amount.MinorUnits,
            Recipient = recipientCode!,
            Reason = $"Payout {reference}",
            Reference = reference
        };

        var response =
            await _externalApiClient.PostAsync<PaystackTransferRequest, PaystackTransferResponse>(
                transferUrl,
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
                Message = "No response from Paystack."
            };
        }

        if (!response.Status || response.Data is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = response.Message ?? "Unable to initiate transfer."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = NormalizeStatus(response.Data.Status),
            Reference = response.Data.Reference ?? reference,
            Message = response.Message ?? "Transfer initiated."
        };
    }

    public async Task<TransferResult> VerifyTransferAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfer/verify/{Uri.EscapeDataString(reference)}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var response =
            await _externalApiClient.GetAsync<PaystackTransferResponse>(
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
                Message = "No response from Paystack."
            };
        }

        if (!response.Status || response.Data is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = response.Message ?? "Unable to verify transfer."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = NormalizeStatus(response.Data.Status),
            Reference = response.Data.Reference ?? reference,
            Message = response.Message ?? "Transfer verified."
        };
    }

    private async Task<TransferResult> CreateRecipientAsync(
        BankAccount bankAccount,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transferrecipient";

        var payload = new PaystackCreateRecipientRequest
        {
            Type = "nuban",
            Name = bankAccount.AccountName,
            AccountNumber = bankAccount.AccountNumber,
            BankCode = bankAccount.BankCode,
            Currency = string.IsNullOrWhiteSpace(bankAccount.Currency)
                ? "NGN"
                : bankAccount.Currency!
        };

        var response =
            await _externalApiClient.PostAsync<PaystackCreateRecipientRequest, PaystackCreateRecipientResponse>(
                url,
                payload,
                headers,
                cancellationToken);

        if (response is null || !response.Status || response.Data is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = string.Empty,
                Message = response?.Message ?? "Unable to create Paystack recipient."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = "success",
            Reference = response.Data.RecipientCode,
            Message = "Recipient created."
        };
    }

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "pending";

        return status.ToLowerInvariant() switch
        {
            "success" => "success",
            "failed" => "failed",
            "reversed" => "reversed",
            "pending" => "pending",
            "otp" => "pending",
            "processing" => "pending",
            "queued" => "pending",
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

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transaction/verify/{Uri.EscapeDataString(reference)}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var response =
            await _externalApiClient.GetAsync<PaystackVerifyPaymentResponse>(
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
                Message = "No response from Paystack."
            };
        }

        // Paystack returns status = false and message like
        // "Transaction reference not found" when the reference doesn't exist.
        if (!response.Status
            && response.Message?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = true,
                Found = false,
                Status = "not_found",
                Reference = reference,
                Message = response.Message
            };
        }

        if (!response.Status || response.Data is null)
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = false,
                Found = false,
                Status = "pending",
                Reference = reference,
                Message = response.Message ?? "Unable to verify payment."
            };
        }

        return new PaymentVerificationResult
        {
            IsSuccessful = true,
            Found = true,
            Status = NormalizePaymentStatus(response.Data.Status),
            Reference = reference,
            Message = response.Message,
        };
    }


    private static string NormalizePaymentStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "pending";

        return status.ToLowerInvariant() switch
        {
            "success" => "success",
            "failed" => "failed",
            "abandoned" => "failed",
            "reversed" => "failed",
            "ongoing" => "pending",
            "pending" => "pending",
            "queued" => "pending",
            "processing" => "pending",
            _ => "pending"
        };
    }

    // ---------------------------------------------------------------------
    // Response and request DTOs
    // ---------------------------------------------------------------------

    private sealed class PaystackVerifyPaymentResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackVerifyPaymentData? Data { get; set; }
    }

    private sealed class PaystackVerifyPaymentData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("paid_at")]
        public string? PaidAt { get; set; }

        [JsonPropertyName("channel")]
        public string? Channel { get; set; }

        [JsonPropertyName("gateway_response")]
        public string? GatewayResponse { get; set; }

        [JsonPropertyName("customer")]
        public PaystackVerifyPaymentCustomer? Customer { get; set; }
    }

    private sealed class PaystackVerifyPaymentCustomer
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("customer_code")]
        public string? CustomerCode { get; set; }
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

    private sealed class PaystackCreateRecipientRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "nuban";

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("bank_code")]
        public string BankCode { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";
    }

    private sealed class PaystackCreateRecipientResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackCreateRecipientData? Data { get; set; }
    }

    private sealed class PaystackCreateRecipientData
    {
        [JsonPropertyName("recipient_code")]
        public string RecipientCode { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("bank_code")]
        public string BankCode { get; set; } = string.Empty;
    }

    private sealed class PaystackTransferRequest
    {
        [JsonPropertyName("source")]
        public string Source { get; set; } = "balance";

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("recipient")]
        public string Recipient { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;
    }

    private sealed class PaystackTransferResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackTransferData? Data { get; set; }
    }

    private sealed class PaystackTransferData
    {
        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("transfer_code")]
        public string? TransferCode { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("recipient")]
        public string? Recipient { get; set; }
    }
}