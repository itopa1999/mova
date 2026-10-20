using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Payment.Monnify;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService
{
    public async Task<TransferResult> VerifyTransferAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Failed(reference, "Reference is required.");
        }

        var token = await AuthenticateAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return Failed(reference, "Unable to authenticate with Monnify.");
        }

        var response =
            await _externalApiClient.GetAsync<MonnifyTransferResponse>(
                $"{_settings.BaseUrl.TrimEnd('/')}/api/v2/disbursements/single/summary" +
                $"?reference={Uri.EscapeDataString(reference)}",
                Bearer(token),
                cancellationToken);

        return ToTransferResult(
            response,
            reference,
            expectedAmount: null,
            "Unable to verify Monnify transfer.");
    }
}