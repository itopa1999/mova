using Mova.Domain.Entities;

namespace Mova.Application.Interfaces.Identity;

public interface IIdentityService
{
    Task<(bool Success, string ErrorMessage, long UserId)> CreateUserAsync(
        string email,
        string phoneNumber,
        string password,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string ErrorMessage)> AddToRoleAsync(
        long userId,
        string role,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string ErrorMessage)> RemoveFromRoleAsync(
        long userId,
        string role,
        CancellationToken cancellationToken = default);


    Task<(bool Success, string ErrorMessage)> MarkEmailAndPhoneAsVerifiedAsync(long userId, CancellationToken cancellationToken = default);

    Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(long userId, string newPassword, CancellationToken cancellationToken = default);

    Task<(bool Success, string ErrorMessage)> ChangePasswordAsync(
        long userId,
        string oldPassword,
        string newPassword, CancellationToken cancellationToken = default);

    Task<bool> CheckPasswordAsync(long userId, string password, CancellationToken cancellationToken = default);

    Task<bool> IsAccountVerifiedAsync(long userId, CancellationToken cancellationToken = default);

    Task<IList<string>> GetRolesAsync(long userId, CancellationToken cancellationToken = default);

    Task<bool> CreditBalanceAsync(User user, decimal Amount, CancellationToken cancellationToken);

    Task<bool> DebitBalanceAsync(User user, decimal amount, CancellationToken cancellationToken);
    Task<int> CountUsersInRoleAsync(string role, CancellationToken cancellationToken = default);


    Task<bool> IsLockedOutAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLockoutEndAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task RecordFailedAccessAsync(
        long userId,
        CancellationToken cancellationToken = default);

    Task ResetFailedAccessAsync(
        long userId,
        CancellationToken cancellationToken = default);


}