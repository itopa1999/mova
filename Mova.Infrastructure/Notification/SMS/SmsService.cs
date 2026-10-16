using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.Notification;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Notification.Sms;

public sealed class SmsService : ISmsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TermiiSettings _settings;
    private readonly ILogger<SmsService> _logger;

    public SmsService(
        IHttpClientFactory httpClientFactory,
        IOptions<TermiiSettings> settings,
        ILogger<SmsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendOtpAsync(
        string phoneNumber,
        string otp,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        using var op = OperationLogger.Start(
            _logger,
            "TermiiSendOtp",
            ("Phone", phoneNumber),
            ("Purpose", string.IsNullOrWhiteSpace(purpose) ? "(none)" : purpose));

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            op.Warning("Phone number is empty — skipping SMS.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            op.Error("Termii API key is not configured.");
            throw new InvalidOperationException(
                "Termii API key is missing. Configure TermiiSettings:ApiKey.");
        }

        var normalized = NormalizePhoneNumber(phoneNumber);

        if (normalized is null)
        {
            op.Warning($"Phone number '{phoneNumber}' is not a valid Nigerian number — skipping SMS.");
            return;
        }

        var message = BuildMessage(otp, purpose);

        var payload = new TermiiSendRequest
        {
            To = normalized,
            From = _settings.SenderId,
            Sms = message,
            Type = "plain",
            Channel = "dnd",
            ApiKey = _settings.ApiKey,
        };

        var client = _httpClientFactory.CreateClient("Termii");
        var endpoint = $"{_settings.BaseUrl.TrimEnd('/')}/api/sms/send";

        var response = await client.PostAsJsonAsync(endpoint, payload, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            op.Error($"Termii returned HTTP {(int)response.StatusCode}: {body}");
            throw new HttpRequestException(
                $"Termii SMS send failed with status {(int)response.StatusCode}.");
        }

        var result = System.Text.Json.JsonSerializer
            .Deserialize<TermiiSendResponse>(body);

        if (result is null
            || string.IsNullOrWhiteSpace(result.MessageId)
            || (!string.IsNullOrWhiteSpace(result.Message)
                && !result.Message.Contains("Success", StringComparison.OrdinalIgnoreCase)))
        {
            op.Error($"Termii rejected the send: {result?.Message ?? body}");
            throw new InvalidOperationException(
                $"Termii SMS send rejected: {result?.Message ?? "unknown response"}");
        }

        op.Success(
            $"SMS sent to {normalized}. " +
            $"MessageId: {result.MessageId}, Balance: {result.Balance?.ToString() ?? "n/a"}.");
    }

    private static string BuildMessage(string otp, string purpose)
    {
        var purposeLabel = string.IsNullOrWhiteSpace(purpose)
            ? "verification"
            : purpose switch
            {
                "AccountVerification" => "account verification",
                "PasswordReset" => "password reset",
                "TransactionPin" => "transaction PIN",
                _ => "verification",
            };

        return $"Your MOVA {purposeLabel} code is {otp}. " +
               "It expires in 2 minutes. Never share this code with anyone.";
    }

    /// <summary>
    /// Converts a stored phone number to Termii's expected format.
    /// Accepts: +2348064160380, 2348064160380, 08064160380, 8064160380.
    /// Returns: 2348064160380 (or null if the input isn't a Nigerian number).
    /// </summary>
    private static string? NormalizePhoneNumber(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        // +234...  →  234...   (13 digits, already correct)
        if (digits.StartsWith("234") && digits.Length == 13)
        {
            return digits;
        }

        // 0XXXXXXXXXX  →  234XXXXXXXXXX  (11 digits starting with 0)
        if (digits.StartsWith('0') && digits.Length == 11)
        {
            return "234" + digits[1..];
        }

        // XXXXXXXXXX  →  234XXXXXXXXXX  (10 digits, no leading 0)
        if (!digits.StartsWith('0') && digits.Length == 10)
        {
            return "234" + digits;
        }

        // Anything else isn't a valid Nigerian mobile number.
        return null;
    }

    private sealed class TermiiSendRequest
    {
        [JsonPropertyName("to")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("sms")]
        public string Sms { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = "plain";

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = "dnd";

        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = string.Empty;
    }

    private sealed class TermiiSendResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("message_id")]
        public string? MessageId { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("balance")]
        public decimal? Balance { get; set; }
    }
}