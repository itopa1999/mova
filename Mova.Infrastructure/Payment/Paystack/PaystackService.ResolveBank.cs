using System.Text.Json.Serialization;
using System.Web;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    public async Task<ResolveBankAccountResult> ResolveBankAccountAsync(
        string accountNumber,
        string bankCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountNumber) || !accountNumber.All(char.IsDigit))
        {
            return new ResolveBankAccountResult
            {
                CallSucceeded = false,
                Message = "Account number must contain only digits."
            };
        }

        if (string.IsNullOrWhiteSpace(bankCode))
        {
            return new ResolveBankAccountResult
            {
                CallSucceeded = false,
                Message = "Bank code is required."
            };
        }

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

        if (response is null)
        {
            return new ResolveBankAccountResult
            {
                CallSucceeded = false,
                Message = "No response from Paystack."
            };
        }

        if (!response.Status || response.Data is null ||
            string.IsNullOrWhiteSpace(response.Data.AccountName))
        {
            return new ResolveBankAccountResult
            {
                CallSucceeded = false,
                Message = response.Message ?? "Unable to resolve bank account."
            };
        }

        return new ResolveBankAccountResult
        {
            CallSucceeded = true,
            Message = response.Message ?? "Account number resolved.",
            AccountNumber = response.Data.AccountNumber,
            AccountName = response.Data.AccountName,
            BankId = response.Data.BankId
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