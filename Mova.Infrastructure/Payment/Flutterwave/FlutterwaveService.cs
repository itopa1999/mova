using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.ExternalAPI;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;
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

        var expectedBytes =
            Encoding.UTF8.GetBytes(
                _settings.SecretKey.Trim());

        var actualBytes =
            Encoding.UTF8.GetBytes(
                signature.Trim());

        return Task.FromResult(
            CryptographicOperations.FixedTimeEquals(
                expectedBytes,
                actualBytes));
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
        var recipientId = bankAccount.FlutterwaveRecipientId;

        if (string.IsNullOrWhiteSpace(recipientId))
        {
            var recipientResult = await CreateRecipientAsync(
                bankAccount, headers, cancellationToken);

            if (!recipientResult.IsSuccessful)
                return recipientResult;

            recipientId = recipientResult.Reference;

            bankAccount.FlutterwaveRecipientId = recipientId;
        }

        // Step 2 — initiate the transfer.
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfers";

        var payload = new FlutterwaveTransferRequest
        {
            AccountBank = bankAccount.BankCode,
            AccountNumber = bankAccount.AccountNumber,
            Narration = $"Payout {reference}",
            Currency = string.IsNullOrWhiteSpace(bankAccount.Currency)
                ? "NGN"
                : bankAccount.Currency!,
            Amount = amount.ToDecimal(),
            Reference = reference,
            CallbackUrl = _externalApiSettings.PaymentCallbackUrl
        };

        var response =
            await _externalApiClient.PostAsync<
                FlutterwaveTransferRequest,
                FlutterwaveTransferResponse>(
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
                Message = "No response from Flutterwave."
            };
        }

        if (response.Status != "success" || response.Data is null)
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
        // Flutterwave's verify endpoint is by ID, not reference.
        // We query the list of transfers by reference and take the first.
        var query =
            $"?reference={Uri.EscapeDataString(reference)}";

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfers{query}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var response =
            await _externalApiClient.GetAsync<FlutterwaveTransferListResponse>(
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
                Message = "No response from Flutterwave."
            };
        }

        if (response.Status != "success"
            || response.Data is null
            || response.Data.Count == 0)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = reference,
                Message = response.Message ?? "Transfer not found."
            };
        }

        var transfer = response.Data[0];

        return new TransferResult
        {
            IsSuccessful = true,
            Status = NormalizeStatus(transfer.Status),
            Reference = transfer.Reference ?? reference,
            Message = response.Message ?? "Transfer verified."
        };
    }

    private async Task<TransferResult> CreateRecipientAsync(
        BankAccount bankAccount,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/transfers/recipients";

        var payload = new FlutterwaveCreateRecipientRequest
        {
            Type = "bank",
            Name = bankAccount.AccountName,
            AccountNumber = bankAccount.AccountNumber,
            BankCode = bankAccount.BankCode,
            Currency = string.IsNullOrWhiteSpace(bankAccount.Currency)
                ? "NGN"
                : bankAccount.Currency!
        };

        var response =
            await _externalApiClient.PostAsync<
                FlutterwaveCreateRecipientRequest,
                FlutterwaveCreateRecipientResponse>(
                    url,
                    payload,
                    headers,
                    cancellationToken);

        if (response is null
            || response.Status != "success"
            || response.Data is null)
        {
            return new TransferResult
            {
                IsSuccessful = false,
                Status = "failed",
                Reference = string.Empty,
                Message = response?.Message
                    ?? "Unable to create Flutterwave recipient."
            };
        }

        return new TransferResult
        {
            IsSuccessful = true,
            Status = "success",
            Reference = response.Data.Id.ToString(),
            Message = "Recipient created."
        };
    }

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "pending";

        return status.ToLowerInvariant() switch
        {
            "successful" => "success",
            "success" => "success",
            "failed" => "failed",
            "reversed" => "reversed",
            "pending" => "pending",
            "new" => "pending",
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
            $"{_settings.BaseUrl.TrimEnd('/')}/transactions/verify_by_reference" +
            $"?tx_ref={Uri.EscapeDataString(reference)}";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var response =
            await _externalApiClient.GetAsync<FlutterwaveVerifyPaymentResponse>(
                url,
                headers,
                cancellationToken);

        Console.WriteLine("===== FLUTTERWAVE VERIFY RESPONSE4 =====");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
            response,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("====================================");

        if (response is null)
        {
            return new PaymentVerificationResult
            {
                IsSuccessful = false,
                Found = false,
                Status = "pending",
                Reference = reference,
                Message = "No response from Flutterwave."
            };
        }

        // Flutterwave returns status = "error" and message like
        // "No transaction was found for this tx_ref" when the reference
        // doesn't exist. That's a genuine "not found" — not a transport error.
        if (string.Equals(response.Status, "error", StringComparison.OrdinalIgnoreCase)
            && response.Message?.Contains("No transaction", StringComparison.OrdinalIgnoreCase) == true)
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

        if (!string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase)
            || response.Data is null)
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
            "successful" => "success",
            "success" => "success",
            "completed" => "success",
            "failed" => "failed",
            "cancelled" => "failed",
            "pending" => "pending",
            "processing" => "pending",
            "new" => "pending",
            _ => "pending"
        };
    }

    // ---------------------------------------------------------------------
    // Request / Response DTOs
    // ---------------------------------------------------------------------

    private sealed class FlutterwaveVerifyPaymentResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public FlutterwaveVerifyPaymentData? Data { get; set; }
    }

    private sealed class FlutterwaveVerifyPaymentData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("tx_ref")]
        public string? TxRef { get; set; }

        [JsonPropertyName("flw_ref")]
        public string? FlwRef { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public decimal? Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("charged_amount")]
        public decimal? ChargedAmount { get; set; }

        [JsonPropertyName("payment_type")]
        public string? PaymentType { get; set; }
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

    private sealed class FlutterwaveCreateRecipientRequest
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "bank";

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("bank_code")]
        public string BankCode { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";
    }

    private sealed class FlutterwaveCreateRecipientResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public FlutterwaveRecipientData? Data { get; set; }
    }

    private sealed class FlutterwaveRecipientData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("account_number")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("bank_code")]
        public string? BankCode { get; set; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }
    }

    private sealed class FlutterwaveTransferRequest
    {
        [JsonPropertyName("account_bank")]
        public string AccountBank { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("narration")]
        public string Narration { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = "NGN";

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;

        [JsonPropertyName("callback_url")]
        public string? CallbackUrl { get; set; }
    }

    private sealed class FlutterwaveTransferResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public FlutterwaveTransferData? Data { get; set; }
    }

    private sealed class FlutterwaveTransferData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("complete_message")]
        public string? CompleteMessage { get; set; }
    }

    private sealed class FlutterwaveTransferListResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public List<FlutterwaveTransferData>? Data { get; set; }
    }
}