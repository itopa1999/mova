using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Behaviors;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Constants;

namespace Mova.Infrastructure.Services.Security;

public class TransactionPinService : ITransactionPinService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ITransactionPinAttemptStore _pinAttemptStore;
    private readonly IPinChallenge _pinChallenge;

    public TransactionPinService(
        ApplicationDbContext context,
        IPasswordHasher<User> passwordHasher,
        ITransactionPinAttemptStore pinAttemptStore,
        IPinChallenge pinChallenge)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _pinAttemptStore = pinAttemptStore;
        _pinChallenge = pinChallenge;
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

        await _pinAttemptStore.ResetAsync(UserPublicId, cancellationToken);
    }

    public async Task<bool> VerifyPinAsync(
        string UserPublicId,
        string pin,
        CancellationToken cancellationToken = default)
    {
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
        return isValid;
    }

    public async Task<TransactionPinVerificationResult> VerifyPinWithStatusAsync(
        string UserPublicId,
        string pin,
        PinChallengeType pinChallengeType,
        CancellationToken cancellationToken = default)
    {
        if (await _pinAttemptStore.IsLockedAsync(
                UserPublicId,
                cancellationToken))
        {
            return TransactionPinVerificationResult.Locked;
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(
                x => x.PublicId == UserPublicId,
                cancellationToken);

        if (user is null ||
            string.IsNullOrWhiteSpace(user.TransactionPinHash))
        {
            return TransactionPinVerificationResult.Invalid;
        }

        var result = _passwordHasher.VerifyHashedPassword(
            user,
            user.TransactionPinHash,
            pin);

        var isValid = result == PasswordVerificationResult.Success ||
                      result == PasswordVerificationResult.SuccessRehashNeeded;

        if (isValid)
        {
            var wasReset = await _pinAttemptStore.ResetAfterSuccessfulVerificationAsync(
                UserPublicId,
                cancellationToken);

            if (!wasReset)
            {
                return TransactionPinVerificationResult.Locked;
            }

            var challengeCreated = await _pinChallenge.CreateAsync(
                UserPublicId,
                pinChallengeType,
                cancellationToken);

            if (!challengeCreated)
            {
                throw new PinChallengePersistenceException(
                    $"Failed to persist PIN challenge for user {UserPublicId} " +
                    $"(type: {pinChallengeType}).");
            }

            return TransactionPinVerificationResult.Verified;
        }

        var outcome = await _pinAttemptStore.RecordFailureAsync(
            UserPublicId,
            cancellationToken);

        if (outcome == TransactionPinFailureOutcome.LockedNow)
        {
            return TransactionPinVerificationResult.LockedNow;
        }

        if (outcome == TransactionPinFailureOutcome.Warning)
        {
            return TransactionPinVerificationResult.InvalidFinalWarning;
        }

        return await _pinAttemptStore.IsLockedAsync(UserPublicId, cancellationToken)
            ? TransactionPinVerificationResult.Locked
            : TransactionPinVerificationResult.Invalid;
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
}