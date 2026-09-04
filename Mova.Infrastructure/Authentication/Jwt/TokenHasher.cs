using System.Security.Cryptography;
using System.Text;

namespace Mova.Infrastructure.Authentication.Jwt;

public sealed class TokenHasher : ITokenHasher
{
    public string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);

        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }
}