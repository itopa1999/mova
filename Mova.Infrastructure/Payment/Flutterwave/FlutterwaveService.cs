using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.Payment;

namespace Mova.Infrastructure.Payment.Flutterwave;

public sealed class FlutterwaveService(
    IOptions<FlutterwaveSettings> options) : IFlutterwaveService
{
    private readonly FlutterwaveSettings _settings = options.Value;

    public Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature)
    {
        if (string.IsNullOrWhiteSpace(_settings.SecretKey)
            || string.IsNullOrWhiteSpace(signature))
        {
            return Task.FromResult(false);
        }

        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(_settings.SecretKey));

        var expectedSignature = Convert.ToBase64String(
            hmac.ComputeHash(rawBody));

        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        var actualBytes = Encoding.UTF8.GetBytes(signature.Trim());

        return Task.FromResult(
            CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes));
    }
}
