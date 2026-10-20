using Mova.Application.Interfaces.Payment;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Payment;

public sealed partial class FlutterwaveService
{
    public async Task<TransferResult> VerifyTransferAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Reference is required."
            };
        }

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
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "No response from Flutterwave."
            };
        }

        if (!string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return new TransferResult
            {
                CallSucceeded = false,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = response.Message ?? "Unable to verify transfer."
            };
        }

        if (response.Data is null || response.Data.Count == 0)
        {
            return new TransferResult
            {
                CallSucceeded = true,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "Transfer not found yet."
            };
        }

        var transfer = response.Data
            .FirstOrDefault(t =>
                string.Equals(t.Reference, reference, StringComparison.Ordinal));

        if (transfer is null)
        {
            return new TransferResult
            {
                CallSucceeded = true,
                Found = false,
                Status = TransactionStatus.Pending,
                Reference = reference,
                Message = "No transfer found with the supplied reference."
            };
        }

        return new TransferResult
        {
            CallSucceeded = true,
            Found = true,
            Status = FlutterwaveTransferStatusMapper.NormalizeStatus(transfer.Status),
            Reference = transfer.Reference!,
            Message = transfer.CompleteMessage
                ?? response.Message
                ?? "Transfer verified."
        };
    }
}