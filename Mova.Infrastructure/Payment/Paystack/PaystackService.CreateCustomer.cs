using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    public async Task<PaystackCustomerResult> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return new PaystackCustomerResult
            {
                CallSucceeded = false,
                Message = "A valid email address is required."
            };
        }

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

        if (response is null)
        {
            return new PaystackCustomerResult
            {
                CallSucceeded = false,
                Message = "No response from Paystack."
            };
        }

        if (!response.Status || response.Data is null ||
            string.IsNullOrWhiteSpace(response.Data.CustomerCode))
        {
            return new PaystackCustomerResult
            {
                CallSucceeded = false,
                Message = response.Message ?? "Unable to create Paystack customer."
            };
        }

        if (!string.Equals(response.Data.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            return new PaystackCustomerResult
            {
                CallSucceeded = false,
                Message = "Email mismatch between request and Paystack response."
            };
        }

        return new PaystackCustomerResult
        {
            CallSucceeded = true,
            Message = response.Message ?? "Customer created.",
            CustomerCode = response.Data.CustomerCode,
            CustomerId = response.Data.Id.ToString(),
            Email = response.Data.Email,
            FirstName = response.Data.FirstName ?? firstName,
            LastName = response.Data.LastName ?? lastName,
            Phone = response.Data.Phone ?? phoneNumber
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

// todo
// Check this before shipping:
// Create a customer.
// Create the same customer again with the same data
// Inspect the raw HTTP status and JSON.