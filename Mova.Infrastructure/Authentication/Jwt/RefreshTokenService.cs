using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Authentication.Jwt;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;
    private readonly ITokenHasher _tokenHasher;
    private readonly JwtSettings _jwtSettings;
    private readonly ApplicationDbContext _context;

    public RefreshTokenService(
        IRefreshTokenGenerator refreshTokenGenerator,
        ITokenHasher tokenHasher,
        IOptions<JwtSettings> jwtOptions,
        ApplicationDbContext context)
    {
        _refreshTokenGenerator = refreshTokenGenerator;
        _tokenHasher = tokenHasher;
        _jwtSettings = jwtOptions.Value;
        _context = context;
    }

    public Task<(string Token, RefreshToken RefreshToken)> CreateAsync(
        string userPublicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = _refreshTokenGenerator.Generate();

        var tokenHash = _tokenHasher.HashToken(token);

        var refreshToken = new RefreshToken
        {
            UserPublicId = userPublicId,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(
                _jwtSettings.RefreshTokenExpiryDays)
        };

        return Task.FromResult((token, refreshToken));
    }

    public async Task<RefreshToken?> ValidateAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = _tokenHasher.HashToken(refreshToken);

        var token = await _context.RefreshTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (token is null)
        {
            return null;
        }

        if (!token.IsActive)
        {
            return null;
        }

        return token;
    }

    public Task RevokeAsync(
        RefreshToken refreshToken,
        string? revokedByIp = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        refreshToken.RevokedAt = DateTimeOffset.UtcNow;
        refreshToken.RevokedByIp = revokedByIp;

        refreshToken.RevocationReason = reason;

        return Task.CompletedTask;
    }

    public async Task<(string Token, RefreshToken RefreshToken)> RotateAsync(
        RefreshToken refreshToken,
        CancellationToken cancellationToken = default)
    {
        await RevokeAsync(
            refreshToken,
            reason: "Refresh token rotation",
            cancellationToken: cancellationToken);

        var newRefreshToken = await CreateAsync(
            refreshToken.UserPublicId,
            cancellationToken);

        refreshToken.ReplacedByTokenHash =
            newRefreshToken.RefreshToken.TokenHash;

        return newRefreshToken;
    }
}
