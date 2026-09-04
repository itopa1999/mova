namespace Mova.Application.Interfaces.Identity;

public interface IIdentityService
{
    Task<(bool Success, string ErrorMessage, string UserPublicId, long UserId)> CreateUserAsync(
        string firstName,
        string lastName,
        string email,
        string phoneNumber,
        string password);

    Task<(bool Success, string ErrorMessage)> AddToRoleAsync(
        long userId,
        string role);

    Task<UserIdentityDto?> GetByIdentifierAsync(
        string identifier,
        CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(
        string email,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);

    Task<bool> PhoneExistsAsync(
        string phoneNumber,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);


    Task<(bool Success, string ErrorMessage)> MarkEmailAndPhoneAsVerifiedAsync(long userId);

    Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(long userId, string newPassword);

    Task<(bool Success, string ErrorMessage)> ChangePasswordAsync(
        long userId,
        string oldPassword,
        string newPassword);

    Task<bool> CheckPasswordAsync(long userId, string password);

    Task<bool> IsAccountVerifiedAsync(long userId);

    Task<IList<string>> GetRolesAsync(long userId);

    Task<bool> UpdateBalanceAsync(string UserPublicId, decimal Amount, CancellationToken cancellationToken);

    Task<bool> DebitBalanceAsync(string userPublicId, decimal amount, CancellationToken cancellationToken);
}