

using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    public async Task<PaystackCustomerResult?> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/customer";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var payload = new PaystackCreateCustomerRequest
        {
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Phone = phoneNumber
        };

        var response =
            await _externalApiClient.PostAsync<
                PaystackCreateCustomerRequest,
                PaystackCreateCustomerResponse>(
                url,
                payload,
                headers,
                cancellationToken);

        if (response is null || !response.Status || response.Data is null)
            return null;

        return new PaystackCustomerResult
        {
            CustomerCode = response.Data.CustomerCode,
            CustomerId = response.Data.Id.ToString(),
            Email = response.Data.Email ?? email,
            FirstName = firstName,
            LastName = lastName,
            Phone = phoneNumber
        };
    }

    private sealed class PaystackCreateCustomerRequest
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("last_name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("phone")]
        public string Phone { get; set; } = string.Empty;
    }

    private sealed class PaystackCreateCustomerResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackCreateCustomerData? Data { get; set; }
    }

    private sealed class PaystackCreateCustomerData
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("customer_code")]
        public string CustomerCode { get; set; } = string.Empty;

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("phone")]
        public string? Phone { get; set; }
    }
}