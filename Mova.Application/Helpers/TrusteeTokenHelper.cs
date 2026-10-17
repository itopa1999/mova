using System.Security.Cryptography;
using System.Text;

namespace Mova.Application.Helpers;

internal static class TrusteeTokenHelper
{
    private const int TokenByteLength = 32;

    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[TokenByteLength];
        RandomNumberGenerator.Fill(bytes);

        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static byte[] Hash(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token cannot be empty.", nameof(token));

        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}