using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Security;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Services.Security;

public class TransactionPinService : ITransactionPinService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;

    public TransactionPinService(
        ApplicationDbContext context,
        IPasswordHasher<User> passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
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

        await _context.SaveChangesAsync(cancellationToken);
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

        return result == PasswordVerificationResult.Success ||
               result == PasswordVerificationResult.SuccessRehashNeeded;
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

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void ValidatePin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            throw new ArgumentException("PIN is required.");
        }

        if (pin.Length != 6 ||
            !pin.All(char.IsDigit))
        {
            throw new ArgumentException(
                "PIN must contain exactly 6 digits.");
        }
    }
}