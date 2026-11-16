using Mova.Domain.Enums;
namespace Mova.Application.Interfaces.Admin;

public interface IAdminIdentityService
{
    Task<(IReadOnlyList<AdminUserListDto> Items, int TotalCount)> GetFilteredUsersAsync(
        string? search = null,
        OnboardingStage? onboardingStage = null,
        UserAccountStatus? accountStatus = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);


    // Task<AdminUserListDto?> GetByPublicIdAsync(
    //     string publicId,
    //     CancellationToken cancellationToken = default);
}