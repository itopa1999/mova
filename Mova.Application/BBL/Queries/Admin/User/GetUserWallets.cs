using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Admin;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Queries.Admin.User;

public sealed class GetUserWallets
{
    public sealed class Query : IRequest<BaseResult<BasePaginationResponse<UserWalletDto>>>
    {
        public string UserId { get; init; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int Page { get; init; } = 1;

        [Range(1, 100)]
        public int PageSize { get; init; } = 20;

        public string? Search { get; init; }

        public WalletStatus? Status { get; init; }

        public DateTimeOffset? CreatedFrom { get; init; }

        public DateTimeOffset? CreatedTo { get; init; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<BasePaginationResponse<UserWalletDto>>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAdminIdentityService _adminIdentityService;

        public Handler(IUnitOfWork unitOfWork, IAdminIdentityService adminIdentityService)
        {
            _unitOfWork = unitOfWork;
            _adminIdentityService = adminIdentityService;
        }

        public async Task<BaseResult<BasePaginationResponse<UserWalletDto>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            bool userExists = await _adminIdentityService.GetUserAsync(request.UserId, cancellationToken);

            if (!userExists)
            {
                return new BaseResult<BasePaginationResponse<UserWalletDto>>
                (
                    HttpStatusCode.NotFound,
                    "User not found.",
                    null
                );
            }

            if (request.CreatedFrom.HasValue
                && request.CreatedTo.HasValue
                && request.CreatedFrom.Value > request.CreatedTo.Value)
            {
                return new BaseResult<BasePaginationResponse<UserWalletDto>>
                (
                    HttpStatusCode.BadRequest,
                    "CreatedFrom must be earlier than or equal to CreatedTo.",
                    null
                );
            }

            var page = request.Page < 1 ? 1 : request.Page;
            var pageSize = request.PageSize switch
            {
                < 1 => 20,
                > 100 => 100,
                _ => request.PageSize,
            };

            var baseQuery = _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(w => w.UserPublicId == request.UserId);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var term = request.Search.Trim().ToLower();
                baseQuery = baseQuery.Where(w => w.Name.ToLower().Contains(term));
            }

            if (request.Status.HasValue)
            {
                baseQuery = baseQuery.Where(w => w.Status == request.Status.Value);
            }

            if (request.CreatedFrom.HasValue)
            {
                baseQuery = baseQuery.Where(w => w.CreatedAt >= request.CreatedFrom.Value);
            }

            if (request.CreatedTo.HasValue)
            {
                baseQuery = baseQuery.Where(w => w.CreatedAt <= request.CreatedTo.Value);
            }

            var totalCount = await baseQuery.CountAsync(cancellationToken);

            var wallets = await baseQuery
                .OrderByDescending(w => w.CreatedAt)
                .ThenBy(w => w.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(w => new UserWalletDto
                {
                    Id = w.Id.ToString(),
                    PublicId = w.UserPublicId,
                    Name = w.Name,
                    Description = w.Description,
                    Type = w.Type.ToString(),
                    Status = w.Status.ToString(),
                    Category = w.Category.Name,
                    PayoutDestination = w.PayoutDestination.ToString(),
                    RestartCount = w.RestartCount,

                    TargetAmount = new UserWalletMoneyDto(w.TargetAmount.ToDecimal(), w.TargetAmount.Currency),
                    FundedAmount = new UserWalletMoneyDto(w.FundedAmount.ToDecimal(), w.FundedAmount.Currency),
                    LockedAmount = new UserWalletMoneyDto(w.LockedAmount.ToDecimal(), w.LockedAmount.Currency),
                    AvailableAmount = new UserWalletMoneyDto(w.AvailableAmount.ToDecimal(), w.AvailableAmount.Currency),
                    TotalReleasedAmount = new UserWalletMoneyDto(w.TotalReleasedAmount.ToDecimal(), w.TotalReleasedAmount.Currency),
                    TotalWithdrawnAmount = new UserWalletMoneyDto(w.TotalWithdrawnAmount.ToDecimal(), w.TotalWithdrawnAmount.Currency),

                    BankName = w.BankAccount != null ? w.BankAccount.BankName : null,
                    BankAccountNumber = w.BankAccount != null ? w.BankAccount.AccountNumber : null,
                    TrusteeName = w.Trustee != null ? w.Trustee.Name : null,
                    SavingsCircleName = w.SavingsCircle != null ? w.SavingsCircle.Name : null,

                    CreatedAt = w.CreatedAt,
                    CompletedAt = w.CompletedAt,
                    ClosedAt = w.ClosedAt,

                    ModifiedAt = w.ModifiedAt,
                    IsDeleted = w.IsDeleted,
                    DeletedAt = w.DeletedAt,
                    CreatedBy = w.CreatedBy,
                    ModifiedBy = w.ModifiedBy,
                    DeletedBy = w.DeletedBy,
                })
                .ToListAsync(cancellationToken);

            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            return new BaseResult<BasePaginationResponse<UserWalletDto>>
            (
                HttpStatusCode.OK,
                "User wallets retrieved successfully.",
                new BasePaginationResponse<UserWalletDto>
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Items = wallets,
                }
            );
        }
    }
}

public sealed record UserWalletMoneyDto(decimal Amount, string Currency);

public sealed record UserWalletDto
{
    public string Id { get; init; } = string.Empty;
    public string PublicId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string PayoutDestination { get; init; } = string.Empty;
    public int RestartCount { get; init; }

    public UserWalletMoneyDto TargetAmount { get; init; } = new(0, "NGN");
    public UserWalletMoneyDto FundedAmount { get; init; } = new(0, "NGN");
    public UserWalletMoneyDto LockedAmount { get; init; } = new(0, "NGN");
    public UserWalletMoneyDto AvailableAmount { get; init; } = new(0, "NGN");
    public UserWalletMoneyDto TotalReleasedAmount { get; init; } = new(0, "NGN");
    public UserWalletMoneyDto TotalWithdrawnAmount { get; init; } = new(0, "NGN");

    public string? BankName { get; init; }
    public string? BankAccountNumber { get; init; }
    public string? TrusteeName { get; init; }
    public string? SavingsCircleName { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? ModifiedBy { get; init; }
    public string? DeletedBy { get; init; }
}