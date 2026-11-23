using Microsoft.EntityFrameworkCore;
using Mova.Domain.Enums;
using Mova.Application.Interfaces.Admin;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.AdminServices;

public class AdminIdentityService : IAdminIdentityService
{
    private readonly ApplicationDbContext _context;

    public AdminIdentityService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<AdminUserListDto> Items, int TotalCount)> GetFilteredUsersAsync(
        string? search = null,
        OnboardingStage? onboardingStage = null,
        UserAccountStatus? accountStatus = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Users.AsNoTracking().IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            query = query.Where(u =>
                (u.FirstName != null && u.FirstName.Contains(term)) ||
                (u.LastName != null && u.LastName.Contains(term)) ||
                (u.OtherNames != null && u.OtherNames.Contains(term)) ||
                (u.Email != null && u.Email.Contains(term)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(term)) ||
                (u.PublicId != null && u.PublicId.Contains(term))
            );
        }

        if (onboardingStage.HasValue)
        {
            query = query.Where(u => u.OnboardingStage == onboardingStage.Value);
        }

        if (accountStatus.HasValue)
        {
            query = query.Where(u => u.AccountStatus == accountStatus.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rawUsers = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.PublicId,
                u.FirstName,
                u.OtherNames,
                u.LastName,
                u.Email,
                u.PhoneNumber,
                u.AccountStatus,
                u.OnboardingStage,
                u.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var items = rawUsers.Select(u => new AdminUserListDto
        {
            Id = u.Id,
            PublicId = u.PublicId,
            FullName = string.Join(
                " ",
                new[] { u.FirstName, u.OtherNames, u.LastName }
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
            Email = u.Email ?? string.Empty,
            PhoneNumber = u.PhoneNumber ?? string.Empty,
            Status = u.AccountStatus.ToString(),
            OnboardingStage = u.OnboardingStage.ToString(),
            DateJoined = u.CreatedAt
        }).ToList();

        return (items, totalCount);
    }

    public async Task<UserDetailsDto?> GetByPublicIdAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => u.PublicId == publicId)
            .Select(u => new UserDetailsDto
            {
                Id = u.Id,
                PublicId = u.PublicId,

                FirstName = u.FirstName,
                OtherNames = u.OtherNames,
                LastName = u.LastName,
                FullName = u.FullName,
                ProfilePicture = u.ProfilePicture,

                Email = u.Email ?? string.Empty,
                PhoneNumber = u.PhoneNumber,

                EmailConfirmed = u.EmailConfirmed,
                PhoneNumberConfirmed = u.PhoneNumberConfirmed,
                BvnVerified = !string.IsNullOrWhiteSpace(u.BvnHash),

                AccountStatus = u.AccountStatus.ToString(),
                OnboardingStage = u.OnboardingStage.ToString(),

                Balance = u.Balance.ToDecimal(),
                BalanceCurrency = u.Balance.Currency,

                TransactionPinIsSet = !string.IsNullOrWhiteSpace(u.TransactionPinHash),
                TransactionPinSetAt = u.TransactionPinSetAt,
                TransactionPinChangedAt = u.TransactionPinChangedAt,
                TransactionPinResetAt = u.TransactionPinResetAt,

                LastKnownDeviceId = u.LastKnownDeviceId,

                NotifyLoginAlerts = u.NotifyLoginAlerts,
                NotifyReleaseAlerts = u.NotifyReleaseAlerts,
                NotifyProductUpdates = u.NotifyProductUpdates,
                NotifyPromotions = u.NotifyPromotions,

                RestrictionReason = u.RestrictionReason.HasValue
                    ? u.RestrictionReason.Value.ToString()
                    : null,

                RestrictionReasonDetails = u.RestrictionReasonDetails,
                RestrictedAt = u.RestrictedAt,
                RestrictedBy = u.RestrictedBy,
                RestrictionExpiresAt = u.RestrictionExpiresAt,
                ReactivatedAt = u.ReactivatedAt,

                CreatedAt = u.CreatedAt,
                CreatedBy = u.CreatedBy,
                ModifiedAt = u.ModifiedAt,
                ModifiedBy = u.ModifiedBy,
                IsDeleted = u.IsDeleted,
                DeletedAt = u.DeletedAt,
                DeletedBy = u.DeletedBy
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return null;
        }

        var roles = await (
            from userRole in _context.UserRoles.AsNoTracking()
            join role in _context.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where userRole.UserId == user.Id
            select new UserRoleDto
            {
                Id = role.Id.ToString(),
                Name = role.Name ?? string.Empty,
            }
        ).ToListAsync(cancellationToken);

        user.Roles = roles;
        return user;
    }

    public async Task<bool> GetUserAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(u => u.PublicId == publicId, cancellationToken);
    }
}