using System.Security.Cryptography;
using System.Text;

namespace Mova.Infrastructure.Payment;

public sealed partial class PaystackService
{
    public Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature)
    {
        var secretKeyBytes =
            Encoding.UTF8.GetBytes(_settings.SecretKey);

        using var hmac = new HMACSHA512(secretKeyBytes);

        var hashBytes = hmac.ComputeHash(rawBody);

        var expectedSignature =
            Convert.ToHexString(hashBytes).ToLowerInvariant();

        var result = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signature));

        return Task.FromResult(result);
    }
}
