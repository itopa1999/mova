using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment.Paystack;

public sealed class PaystackService(
    IOptions<PaystackSettings> options) : IPaystackService
{
    private readonly PaystackSettings _settings = options.Value;

    public async Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature)
    {
        var secretKeyBytes =
            Encoding.UTF8.GetBytes(_settings.SecretKey);

        using var hmac = new HMACSHA512(secretKeyBytes);

        var hashBytes = hmac.ComputeHash(rawBody);

        var expectedSignature =
            Convert.ToHexString(hashBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signature));
    }
}