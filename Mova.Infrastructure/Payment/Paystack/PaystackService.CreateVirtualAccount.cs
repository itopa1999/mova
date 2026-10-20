

using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    public async Task<PaystackDedicatedAccountResult?> CreateDedicatedVirtualAccountAsync(
        string customerCode,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{_settings.BaseUrl.TrimEnd('/')}/dedicated_account";

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_settings.SecretKey}"
        };

        var payload = new PaystackCreateDedicatedAccountRequest
        {
            Customer = customerCode
        };

        var response =
            await _externalApiClient.PostAsync<
                PaystackCreateDedicatedAccountRequest,
                PaystackCreateDedicatedAccountResponse>(
                url,
                payload,
                headers,
                cancellationToken);

        if (response is null || !response.Status || response.Data is null)
            return null;

        return new PaystackDedicatedAccountResult
        {
            AccountId = response.Data.Id.ToString(),
            AccountNumber = response.Data.AccountNumber,
            AccountName = response.Data.AccountName,
            BankName = response.Data.Bank?.Name ?? string.Empty,
            Currency = response.Data.Currency ?? "NGN",
            CustomerCode = response.Data.Customer?.CustomerCode ?? customerCode
        };
    }

    private sealed class PaystackCreateDedicatedAccountRequest
    {
        [JsonPropertyName("customer")]
        public string Customer { get; set; } = string.Empty;
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