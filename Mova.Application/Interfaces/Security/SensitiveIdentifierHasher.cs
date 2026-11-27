
using System.Security.Cryptography;
using System.Text;

namespace Mova.Application.Interfaces.Security;
public sealed class SensitiveIdentifierHasher
{
    private readonly byte[] _key;

    public SensitiveIdentifierHasher(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new ArgumentException("HMAC key is required.");

        _key = Convert.FromBase64String(base64Key);

        if (_key.Length < 32)
            throw new ArgumentException(
                "HMAC key must contain at least 32 random bytes.");
    }

    public string Hash(string identifier, string purpose)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Identifier is required.");

        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Purpose is required.");

        var normalized = identifier.Trim();

        var payload = Encoding.UTF8.GetBytes(
            $"{purpose.Trim().ToUpperInvariant()}:{normalized}");

        var hash = HMACSHA256.HashData(_key, payload);

        return Convert.ToHexString(hash);
    }
}
