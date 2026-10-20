using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    public async Task<CreateDedicatedAccountResult> CreateDedicatedVirtualAccountAsync(
        string customerCode,
        string? firstName = null,
        string? lastName = null,
        string? phoneNumber = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customerCode))
        {
            return new CreateDedicatedAccountResult
            {
                CallSucceeded = false,
                Message = "Customer code is required."
            };
        }

        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/dedicated_account";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var payload = new PaystackCreateDedicatedAccountRequest
        {
            Customer = customerCode,
            FirstName = firstName,
            LastName = lastName,
            Phone = phoneNumber
        };

        var response =
            await _externalApiClient.PostAsync<
                PaystackCreateDedicatedAccountRequest,
                PaystackCreateDedicatedAccountResponse>(
                url,
                payload,
                headers,
                cancellationToken);

        if (response is null)
        {
            return new CreateDedicatedAccountResult
            {
                CallSucceeded = false,
                Message = "No response from Paystack."
            };
        }

        if (!response.Status || response.Data is null ||
            string.IsNullOrWhiteSpace(response.Data.AccountNumber))
        {
            return new CreateDedicatedAccountResult
            {
                CallSucceeded = false,
                Message = response.Message ?? "Unable to create dedicated virtual account."
            };
        }

        if (!string.Equals(
                response.Data.Customer?.CustomerCode,
                customerCode,
                StringComparison.Ordinal))
        {
            return new CreateDedicatedAccountResult
            {
                CallSucceeded = false,
                Message = "Customer code mismatch between request and Paystack response."
            };
        }

        return new CreateDedicatedAccountResult
        {
            CallSucceeded = true,
            Message = response.Message ?? "Dedicated virtual account created.",
            AccountId = response.Data.Id.ToString(),
            AccountNumber = response.Data.AccountNumber,
            AccountName = response.Data.AccountName,
            BankName = response.Data.Bank?.Name ?? string.Empty,
            Currency = response.Data.Currency ?? string.Empty,
            CustomerCode = response.Data.Customer!.CustomerCode!,
            IsActive = response.Data.Active,
            IsAssigned = response.Data.Assigned
        };
    }

    private sealed class PaystackCreateDedicatedAccountRequest
    {
        [JsonPropertyName("customer")]
        public string Customer { get; set; } = string.Empty;

        [JsonPropertyName("first_name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LastName { get; set; }

        [JsonPropertyName("phone")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Phone { get; set; }
    }

    private sealed class PaystackCreateDedicatedAccountResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PaystackDedicatedAccountData? Data { get; set; }
    }

    private sealed class PaystackDedicatedAccountData
    {
        [JsonPropertyName("bank")]
        public PaystackDedicatedAccountBank? Bank { get; set; }

        [JsonPropertyName("account_name")]
        public string AccountName { get; set; } = string.Empty;

        [JsonPropertyName("account_number")]
        public string AccountNumber { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("assigned")]
        public bool Assigned { get; set; }

        [JsonPropertyName("customer")]
        public PaystackDedicatedAccountCustomer? Customer { get; set; }
    }

    private sealed class PaystackDedicatedAccountBank
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }
    }

    private sealed class PaystackDedicatedAccountCustomer
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("customer_code")]
        public string? CustomerCode { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("phone")]
        public string? Phone { get; set; }
    }
}

// todo
// Calling twice for the same customer — Paystack returns the 
// existing DVA rather than creating a new one. Confirm the second 
// call returns status: true with the same account_number and id as the 
// first. If it returns an error instead, log it distinctly so operators can 
// distinguish "DVA already exists" from a genuine failure.