using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Authentication.Jwt;

public sealed class PinChallengeService : IPinChallenge
{
    private readonly ApplicationDbContext _context;
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(2);

    public PinChallengeService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> CreateAsync(string userPublicId, PinChallengeType pinChallengeType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userPublicId))
            return false;
        var now = DateTimeOffset.UtcNow;

        await _context.PinChallenges
            .Where(c => c.UserPublicId == userPublicId
                        && c.PinChallengeType == pinChallengeType
                        && !c.IsUsed
                        && c.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.IsUsed, true)
                    .SetProperty(c => c.UsedAt, now),
                cancellationToken);

        var challenge = new PinChallenge
        {
            UserPublicId = userPublicId,
            PinChallengeType = pinChallengeType,
            IsUsed = false,
            ExpiresAt = now.Add(ChallengeLifetime),
        };

        await _context.PinChallenges.AddAsync(challenge, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<(bool Success, string Message)> ValidateAsync(string userPublicId, PinChallengeType pinChallengeType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userPublicId))
            return (false, "Please verify your PIN again.");

        var publicId = await FindUserPublicIdAsync(userPublicId, cancellationToken);
        if (string.IsNullOrWhiteSpace(publicId))
        return (false, "Please verify your PIN again.");
        var now = DateTimeOffset.UtcNow;
        var challenge = await _context.PinChallenges
        .Where(c => c.UserPublicId == userPublicId
                    && c.PinChallengeType == pinChallengeType
                    && !c.IsUsed
                    && c.ExpiresAt > now)
        .OrderByDescending(c => c.ExpiresAt)
        .FirstOrDefaultAsync(cancellationToken);

        if (challenge is null)
            return (false, "Please verify your PIN again.");

        var rowsAffected = await _context.PinChallenges
            .Where(c => c.Id == challenge.Id
                        && !c.IsUsed
                        && c.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.IsUsed, true)
                    .SetProperty(c => c.UsedAt, now),
                cancellationToken);

        if (rowsAffected == 0)
            return (false, "Please verify your PIN again.");

        return (true, string.Empty);
    }

    private async Task<string?> FindUserPublicIdAsync(
        string userPublicId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userPublicId)) return null;

        var normalized = userPublicId.Trim();

        return await _context.Users
            .Where(u => u.PublicId == normalized && !u.IsDeleted)
            .Select(u => u.PublicId)
            .FirstOrDefaultAsync(cancellationToken);
    }
    
}