using System.Security.Cryptography;
using System.Text;

namespace Mova.Infrastructure.Payment;

public sealed partial class FlutterwaveService
{
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
}
