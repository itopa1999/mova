using System.Text.Json.Serialization;
using System.Web;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
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
}
