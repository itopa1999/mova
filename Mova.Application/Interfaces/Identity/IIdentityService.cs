namespace Mova.Application.Interfaces.Identity;

public interface IIdentityService
{
    Task<(bool Success, string ErrorMessage, string UserPublicId, long UserId)> CreateUserAsync(
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        string BVN,
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

    Task<UserIdentityDto?> GetByIdentifierAsync(
        string identifier,
        CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(
        string email,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);

    Task<bool> BvnExistsAsync(
        string bvnHash,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);

    Task<bool> PhoneExistsAsync(
        string phoneNumber,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);


    Task<(bool Success, string ErrorMessage)> MarkEmailAndPhoneAsVerifiedAsync(long userId, CancellationToken cancellationToken = default);

    Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(long userId, string newPassword, CancellationToken cancellationToken = default);

    Task<(bool Success, string ErrorMessage)> ChangePasswordAsync(
        long userId,
        string oldPassword,
        string newPassword, CancellationToken cancellationToken = default);

    Task<bool> UpdateNotificationPreferenceAsync(
        string identifier,
        string key,
        bool enabled,
        CancellationToken cancellationToken);

    Task<bool> UpdateLastKnownDeviceAsync(
        string identifier,
        string deviceId,
        CancellationToken cancellationToken= default);

    Task<bool> CheckPasswordAsync(long userId, string password, CancellationToken cancellationToken = default);

    Task<bool> IsAccountVerifiedAsync(long userId, CancellationToken cancellationToken = default);

    Task<IList<string>> GetRolesAsync(long userId, CancellationToken cancellationToken = default);

    Task<bool> CreditBalanceAsync(string UserPublicId, decimal Amount, CancellationToken cancellationToken);

    Task<bool> DebitBalanceAsync(string userPublicId, decimal amount, CancellationToken cancellationToken);
    Task<int> CountUsersInRoleAsync(string role, CancellationToken cancellationToken = default);
}