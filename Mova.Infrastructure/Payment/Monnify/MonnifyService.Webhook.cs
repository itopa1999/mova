using System.Security.Cryptography;
using System.Text;

namespace Mova.Infrastructure.Payment;

public sealed partial class MonnifyService
{
    public Task<bool> VerifyWebhookSignatureAsync(byte[] rawBody, string? signature)
    {
        if (rawBody.Length == 0 || string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(_settings.SecretKey))
            return Task.FromResult(false);

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var expected = Convert.ToHexString(hmac.ComputeHash(rawBody)).ToLowerInvariant();
        var actual = signature.Trim().ToLowerInvariant();
        return Task.FromResult(expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual)));
    }
}
