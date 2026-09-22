using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Common;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Constants;

namespace Mova.Infrastructure.Identity;

public sealed class IdentityService : IIdentityService
{
    private static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromSeconds(60);

    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ICacheService _cache;

    public IdentityService(
        UserManager<User> userManager,
        ApplicationDbContext context,
        ICacheService cache)
    {
        _userManager = userManager;
        _context = context;
        _cache = cache;
    }

    public async Task<(bool Success, string ErrorMessage, string UserPublicId, long UserId)> CreateUserAsync(
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        string password)
    {
        var user = new User
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            UserName = email,
            PhoneNumber = phoneNumber,
            PublicId = string.Empty,
            ProfilePicture = DefaultProfilePictures.PickRandom(),
        };

        var result = await _userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors, string.Empty, 0);
        }

        user.PublicId = user.Id.ToString("D4");

        var updateResult = await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            var errors = string.Join(", ", updateResult.Errors.Select(x => x.Description));
            return (false, errors, string.Empty, 0);
        }

        return (true, string.Empty, user.PublicId, user.Id);
    }

    public async Task<(bool Success, string ErrorMessage)> AddToRoleAsync(long userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return (false, "User not found.");
        }

        var result = await _userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors);
        }

        return (true, string.Empty);
    }

    public async Task<UserIdentityDto?> GetByIdentifierAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        identifier = identifier.Trim();

        return await _cache.GetOrSetFastAsync(
            CacheKeys.ProfileByIdentifier(identifier),
            ct => GetByIdentifierUncachedAsync(identifier, ct),
            timeout: ProfileCacheTtl,
            cancellationToken: cancellationToken);
    }

    private async Task<UserIdentityDto?> GetByIdentifierUncachedAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = identifier.ToUpperInvariant();

        return await _context.Users
            .AsNoTracking()
            .Where(x =>
                x.PublicId == identifier ||
                x.NormalizedEmail == normalizedEmail ||
                x.PhoneNumber == identifier)
            .Select(x => new UserIdentityDto(
                x.Id,
                x.PublicId,
                x.FirstName,
                x.OtherNames,
                x.LastName,
                x.Email,
                x.PhoneNumber,
                x.ProfilePicture,
                x.Balance,
                x.TransactionPinHash ?? string.Empty,
                x.NotifyLoginAlerts,
                x.NotifyReleaseAlerts,
                x.NotifyProductUpdates,
                x.NotifyPromotions,
                x.LastKnownDeviceId ?? string.Empty))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(
        string email,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();

        var query = _context.Users
            .AsNoTracking()
            .Where(x => x.NormalizedEmail == normalizedEmail);

        if (excludeUserId.HasValue)
        {
            query = query.Where(x => x.Id != excludeUserId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> PhoneExistsAsync(
        string phoneNumber,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPhone = phoneNumber.Trim();

        var query = _context.Users
            .AsNoTracking()
            .Where(x => x.PhoneNumber == normalizedPhone);

        if (excludeUserId.HasValue)
        {
            query = query.Where(x => x.Id != excludeUserId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> CheckPasswordAsync(long userId, string password)
    {
        var existingUser = await _userManager.FindByIdAsync(userId.ToString());
        if (existingUser is null) return false;

        return await _userManager.CheckPasswordAsync(existingUser, password);
    }

    public async Task<bool> IsAccountVerifiedAsync(long userId)
    {
        var existingUser = await _userManager.FindByIdAsync(userId.ToString());
        if (existingUser is null) return false;

        return existingUser.EmailConfirmed && existingUser.PhoneNumberConfirmed;
    }

    public async Task<IList<string>> GetRolesAsync(long userId)
    {
        var existingUser = await _userManager.FindByIdAsync(userId.ToString());
        if (existingUser is null) return new List<string>();

        return await _userManager.GetRolesAsync(existingUser);
    }

    public async Task<(bool Success, string ErrorMessage)> MarkEmailAndPhoneAsVerifiedAsync(long userId)
    {
        var existingUser = await _userManager.FindByIdAsync(userId.ToString());
        if (existingUser is null)
        {
            return (false, "User not Found");
        }

        existingUser.EmailConfirmed = true;
        existingUser.PhoneNumberConfirmed = true;

        var result = await _userManager.UpdateAsync(existingUser);

        if (!result.Succeeded)
        {
            return (false, string.Join(", ", result.Errors.Select(x => x.Description)));
        }

        await InvalidateUserCacheAsync(existingUser);

        return (true, string.Empty);
    }

    public async Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(long userId, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return (false, "User not found.");

        var passwordValidator = _userManager.PasswordValidators.FirstOrDefault();
        var validateResult = passwordValidator is null
            ? null
            : await passwordValidator.ValidateAsync(_userManager, user, newPassword);
        if (validateResult is not null && !validateResult.Succeeded)
        {
            var errors = string.Join(", ", validateResult.Errors.Select(e => e.Description));
            return (false, errors);
        }

        var hashedPassword = _userManager.PasswordHasher.HashPassword(user, newPassword);
        user.PasswordHash = hashedPassword;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
            return (false, errors);
        }

        await InvalidateUserCacheAsync(user);

        return (true, string.Empty);
    }

    public async Task<(bool Success, string ErrorMessage)> ChangePasswordAsync(long userId, string oldPassword, string newPassword)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return (false, "User not found.");

        var result = await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors);
        }

        user.SecurityStamp = Guid.NewGuid().ToString();
        await _userManager.UpdateAsync(user);

        await InvalidateUserCacheAsync(user);

        return (true, string.Empty);
    }

    public async Task<bool> CreditBalanceAsync(string UserPublicId, decimal Amount, CancellationToken cancellationToken)
    {
        if (Amount <= 0)
            return false;

        var user = await _context.Users
            .FirstOrDefaultAsync(
                x => x.PublicId == UserPublicId,
                cancellationToken);

        if (user == null)
            return false;

        user.Balance = Money.FromNaira(user.Balance.ToDecimal() + Amount);

        await InvalidateUserCacheAsync(user);

        return true;
    }

    public async Task<bool> DebitBalanceAsync(
        string userPublicId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
            return false;

        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.PublicId == userPublicId, cancellationToken);

        if (user == null || user.Balance.ToDecimal() < amount)
            return false;

        user.Balance = Money.FromNaira(user.Balance.ToDecimal() - amount);

        await InvalidateUserCacheAsync(user);

        return true;
    }

    public async Task<bool> UpdateNotificationPreferenceAsync(
        string identifier,
        string key,
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        identifier = identifier.Trim();
        var normalizedEmail = identifier.ToUpperInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(
                x => x.PublicId == identifier ||
                    x.NormalizedEmail == normalizedEmail ||
                    x.PhoneNumber == identifier,
                cancellationToken);

        if (user is null)
            return false;

        switch (key)
        {
            case "login":
                user.NotifyLoginAlerts = enabled;
                break;

            case "release":
                user.NotifyReleaseAlerts = enabled;
                break;

            case "updates":
                user.NotifyProductUpdates = enabled;
                break;

            case "promotions":
                user.NotifyPromotions = enabled;
                break;

            default:
                return false;
        }

        await _context.SaveChangesAsync(cancellationToken);

        await InvalidateUserCacheAsync(user);

        return true;
    }

    public async Task<bool> UpdateLastKnownDeviceAsync(
        string identifier,
        string deviceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier) ||
            string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        identifier = identifier.Trim();
        var normalizedEmail = identifier.ToUpperInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(
                x => x.PublicId == identifier ||
                    x.NormalizedEmail == normalizedEmail ||
                    x.PhoneNumber == identifier,
                cancellationToken);

        if (user is null)
        {
            return false;
        }

        if (user.LastKnownDeviceId == deviceId)
        {
            return true;
        }

        user.LastKnownDeviceId = deviceId;
        await _context.SaveChangesAsync(cancellationToken);

        await InvalidateUserCacheAsync(user);

        return true;
    }

    private async Task InvalidateUserCacheAsync(User user)
    {
        await _cache.DeleteAsync(CacheKeys.ProfileByIdentifier(user.PublicId));

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            await _cache.DeleteAsync(CacheKeys.ProfileByIdentifier(user.Email));
        }

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            await _cache.DeleteAsync(CacheKeys.ProfileByIdentifier(user.PhoneNumber));
        }
    }
}