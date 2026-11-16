using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Mova.Application.Interfaces.Admin;
using Mova.Domain.Enums;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Queries.Admin.User;

public sealed class GetUsers
{
    public sealed class Query : IRequest<BaseResult<BasePaginationResponse<UserListItemDto>>>
    {
        [Range(1, int.MaxValue)]
        public int Page { get; init; } = 1;

        [Range(1, 100)]
        public int PageSize { get; init; } = 20;

        public string? Search { get; init; }

        public UserAccountStatus? Status { get; init; }

        public OnboardingStage? OnboardingStage { get; init; }
    }

    public sealed class UserListItemDto
    {
        public long Id { get; init; }
        public string PublicId { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string? OtherNames { get; init; }
        public string LastName { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string? Email { get; init; }
        public string? PhoneNumber { get; init; }
        public string UserAccountStatus { get; init; } = string.Empty;
        public string? OnboardingStage { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<BasePaginationResponse<UserListItemDto>>>
    {
        private readonly IAdminIdentityService _adminIdentityService;

        public Handler(IAdminIdentityService adminIdentityService)
        {
            _adminIdentityService = adminIdentityService;
        }

        public async Task<BaseResult<BasePaginationResponse<UserListItemDto>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var page = request.Page < 1 ? 1 : request.Page;
            var pageSize = request.PageSize switch
            {
                < 1 => 20,
                > 100 => 100,
                _ => request.PageSize,
            };

            var (items, totalCount) = await _adminIdentityService.GetFilteredUsersAsync(
                search: request.Search,
                onboardingStage: request.OnboardingStage,
                accountStatus: request.Status,
                page: page,
                pageSize: pageSize,
                cancellationToken: cancellationToken);

            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            var userListItems = items.Select(u =>
            {
                var nameParts = u.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                return new UserListItemDto
                {
                    Id = u.Id,
                    PublicId = u.PublicId,
                    FullName = u.FullName,
                    FirstName = nameParts.FirstOrDefault() ?? string.Empty,
                    LastName = nameParts.Length > 1 ? nameParts.Last() : string.Empty,
                    OtherNames = nameParts.Length > 2 ? string.Join(" ", nameParts.Skip(1).Take(nameParts.Length - 2)) : null,
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    UserAccountStatus = u.Status,
                    OnboardingStage = u.OnboardingStage,
                    CreatedAt = u.DateJoined ?? DateTimeOffset.UtcNow,
                };
            }).ToList();

            return new BaseResult<BasePaginationResponse<UserListItemDto>>(
                HttpStatusCode.OK,
                "Users retrieved successfully.",
                new BasePaginationResponse<UserListItemDto>
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Items = userListItems,
                });
        }
    }
}