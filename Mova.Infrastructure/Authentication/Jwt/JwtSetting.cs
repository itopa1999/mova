namespace Mova.Infrastructure.Authentication.Jwt;

public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public int AccessTokenExpiryMinutes { get; init; } = 15;

    public int RefreshTokenExpiryDays { get; init; } = 7;
    
}