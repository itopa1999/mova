using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Constants;

namespace Mova.Infrastructure.Identity;

public sealed class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public IdentityService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        ICacheService cacheService)
    {
        _userManager = userManager;
        _context = context;
        _cacheService = cacheService;
    }

    // ─────────────────────────────────────────────────────────────
    // WRITE PATH — user creation
    // ─────────────────────────────────────────────────────────────
    public async Task<(bool Success, string ErrorMessage, long UserId)> CreateUserAsync(
        string email,
        string phoneNumber,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            PhoneNumber = phoneNumber
        };

        var result = await _userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors, 0);
        }

        return (true, string.Empty, user.Id);
    }

    // ─────────────────────────────────────────────────────────────
    // ROLE MANAGEMENT
    // ─────────────────────────────────────────────────────────────
    public async Task<(bool Success, string ErrorMessage)> AddToRoleAsync(
        long userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

    public async Task<(bool Success, string ErrorMessage)> RemoveFromRoleAsync(
        long userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return (false, "User not found.");
        }

        var result = await _userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors);
        }

        return (true, string.Empty);
    }

    public async Task<IList<string>> GetRolesAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existingUser = await _userManager.FindByIdAsync(userId.ToString());
        if (existingUser is null) return new List<string>();

        return await _userManager.GetRolesAsync(existingUser);
    }

    public async Task<int> CountUsersInRoleAsync(
        string role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(role))
        {
            return 0;
        }

        var users = await _userManager.GetUsersInRoleAsync(role);
        return users.Count;
    }

    // ─────────────────────────────────────────────────────────────
    // PASSWORD & VERIFICATION
    // ─────────────────────────────────────────────────────────────
    public async Task<bool> CheckPasswordAsync(
        long userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existingUser = await _userManager.FindByIdAsync(userId.ToString());
        if (existingUser is null) return false;

        return await _userManager.CheckPasswordAsync(existingUser, password);
    }

    public async Task<bool> IsAccountVerifiedAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existingUser = await _userManager.FindByIdAsync(userId.ToString());
        if (existingUser is null) return false;

        return existingUser.EmailConfirmed && existingUser.PhoneNumberConfirmed;
    }

    public async Task<(bool Success, string ErrorMessage)> MarkEmailAndPhoneAsVerifiedAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existingUser = await _userManager.FindByIdAsync(userId.ToString());
        if (existingUser is null)
        {
            return (false, "User not Found");
        }

        await _cacheService.DeleteAsync(CacheKeys.UserIdentity(existingUser.Id), cancellationToken);
        
        return (true, string.Empty);
    }

    public async Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(
        long userId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        return (true, string.Empty);
    }

    public async Task<(bool Success, string ErrorMessage)> ChangePasswordAsync(
        long userId,
        string oldPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return (false, "User not found.");

        var result = await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, errors);
        }

        return (true, string.Empty);
    }

    // ─────────────────────────────────────────────────────────────
    // BALANCE
    // ─────────────────────────────────────────────────────────────
    public async Task<bool> CreditBalanceAsync(
        User user,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
            return false;

        user.Balance = Money.FromNaira(user.Balance.ToDecimal() + amount);

        return true;
    }

    public async Task<bool> DebitBalanceAsync(
        User user,
        decimal amount,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
            return false;

        if (user.Balance.ToDecimal() < amount)
            return false;

        user.Balance = Money.FromNaira(user.Balance.ToDecimal() - amount);
        
        return true;
    }


    public async Task<bool> IsLockedOutAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        return await _userManager.IsLockedOutAsync(user);
    }

    public async Task<DateTimeOffset?> GetLockoutEndAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return null;

        return await _userManager.GetLockoutEndDateAsync(user);
    }

    public async Task RecordFailedAccessAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return;

        await _userManager.AccessFailedAsync(user);
    }

    public async Task ResetFailedAccessAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return;

        await _userManager.ResetAccessFailedCountAsync(user);

        await _userManager.SetLockoutEndDateAsync(user, null);
    }
}