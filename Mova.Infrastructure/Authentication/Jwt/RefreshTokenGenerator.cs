using System.Security.Cryptography;

namespace Mova.Infrastructure.Authentication.Jwt;

public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    public string Generate()
    {
        Span<byte> bytes = stackalloc byte[564];

        RandomNumberGenerator.Fill(bytes);

        return Convert.ToBase64String(bytes);
    }
}