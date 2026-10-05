using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Security;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Constants;

namespace Mova.Infrastructure.Services.Security;

public class TransactionPinService : ITransactionPinService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ICacheService _cache;
    private readonly ITransactionPinAttemptStore _pinAttemptStore;

    public TransactionPinService(
        ApplicationDbContext context,
        IPasswordHasher<User> passwordHasher,
        ICacheService cache,
        ITransactionPinAttemptStore pinAttemptStore)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _cache = cache;
        _pinAttemptStore = pinAttemptStore;
    }

    public async Task<bool> HasPinAsync(
        string UserPublicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .AnyAsync(
                x => x.PublicId == UserPublicId &&
                     x.TransactionPinHash != null,
                cancellationToken);
    }

    public async Task SetPinAsync(
        string UserPublicId,
        string pin,
        CancellationToken cancellationToken = default)
    {
        ValidatePin(pin);

        var user = await _context.Users
            .FirstOrDefaultAsync(
                x => x.PublicId == UserPublicId,
                cancellationToken);

        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        if (!string.IsNullOrWhiteSpace(user.TransactionPinHash))
        {
            throw new InvalidOperationException(
                "Transaction PIN has already been set.");
        }

        user.TransactionPinHash =
            _passwordHasher.HashPassword(user, pin);
        user.TransactionPinSetAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await InvalidateUserCacheAsync(user);
        await _pinAttemptStore.ResetAsync(UserPublicId, cancellationToken);
    }

    public async Task<bool> VerifyPinAsync(
        string UserPublicId,
        string pin,
        CancellationToken cancellationToken = default)
    {
        if (await _pinAttemptStore.IsLockedAsync(
                UserPublicId,
                cancellationToken))
        {
            return false;
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(
                x => x.PublicId == UserPublicId,
                cancellationToken);

        if (user is null ||
            string.IsNullOrWhiteSpace(user.TransactionPinHash))
        {
            return false;
        }

        var result = _passwordHasher.VerifyHashedPassword(
            user,
            user.TransactionPinHash,
            pin);

        var isValid = result == PasswordVerificationResult.Success ||
                      result == PasswordVerificationResult.SuccessRehashNeeded;

        if (isValid)
        {
            return await _pinAttemptStore.ResetAfterSuccessfulVerificationAsync(
                UserPublicId,
                cancellationToken);
        }

        await _pinAttemptStore.RecordFailureAsync(UserPublicId, cancellationToken);
        return false;
    }

    public async Task ChangePinAsync(
        string UserPublicId,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        ValidatePin(newPin);

        var user = await _context.Users
            .FirstOrDefaultAsync(
                x => x.PublicId == UserPublicId,
                cancellationToken);

        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        if (string.IsNullOrWhiteSpace(user.TransactionPinHash))
        {
            throw new InvalidOperationException(
                "Transaction PIN has not been set.");
        }

        user.TransactionPinHash =
            _passwordHasher.HashPassword(user, newPin);
        user.TransactionPinChangedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await InvalidateUserCacheAsync(user);
        await _pinAttemptStore.ResetAsync(UserPublicId, cancellationToken);
    }

    public async Task<bool> ResetPinAsync(
        string UserPublicId,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(
                x => x.PublicId == UserPublicId,
                cancellationToken);

        if (user is null) return false;

        user.TransactionPinHash = null;
        user.TransactionPinResetAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        await InvalidateUserCacheAsync(user);
        await _pinAttemptStore.ResetAsync(UserPublicId, cancellationToken);

        return true;
    }

    private static void ValidatePin(string pin)
    {
        if (string.IsNullOrEmpty(pin))
        {
            throw new ArgumentException("PIN is required.");
        }

        if (pin.Length != 6 || pin.Any(c => c < '0' || c > '9'))
        {
            throw new ArgumentException(
                "PIN must contain exactly 6 digits.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // INVALIDATION — matches IdentityService.InvalidateUserCacheAsync
    // so both services invalidate the exact same keys.
    // ─────────────────────────────────────────────────────────────
    private async Task InvalidateUserCacheAsync(User user)
    {
        // Single canonical profile key.
        await _cache.DeleteAsync(CacheKeys.Profile(user.Id));

        // Identifier index entries — short TTL anyway, deleted eagerly
        // so the next lookup re-resolves cleanly.
        if (!string.IsNullOrWhiteSpace(user.PublicId))
        {
            await _cache.DeleteAsync(CacheKeys.ProfileIndex(user.PublicId));
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            await _cache.DeleteAsync(CacheKeys.ProfileIndex(user.Email));
        }

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            await _cache.DeleteAsync(CacheKeys.ProfileIndex(user.PhoneNumber));
        }
    }
}