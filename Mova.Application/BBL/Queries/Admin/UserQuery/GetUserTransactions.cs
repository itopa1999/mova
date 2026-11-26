using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Admin;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Queries.Admin.UserQuery;

public sealed class GetUserTransactions
{
    public sealed class Query : IRequest<BaseResult<BasePaginationResponse<UserTransactionDto>>>
    {
        public string UserId { get; init; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int Page { get; init; } = 1;

        [Range(1, 100)]
        public int PageSize { get; init; } = 20;

        public string? Reference { get; init; }

        public TransactionType? Type { get; init; }

        public TransactionStatus? Status { get; init; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<BasePaginationResponse<UserTransactionDto>>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<BasePaginationResponse<UserTransactionDto>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            bool userExists = await _unitOfWork.Query<User>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(u => u.PublicId == request.UserId, cancellationToken);

            if (!userExists)
            {
                return new BaseResult<BasePaginationResponse<UserTransactionDto>>
                (
                    HttpStatusCode.NotFound,
                    "User not found.",
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

            var baseQuery = _unitOfWork.Query<Transaction>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(t => t.UserPublicId == request.UserId);

            if (!string.IsNullOrWhiteSpace(request.Reference))
            {
                var term = request.Reference.Trim().ToLower();
                baseQuery = baseQuery.Where(t =>
                    t.Reference != null && t.Reference.ToLower().Contains(term));
            }

            if (request.Type.HasValue)
            {
                baseQuery = baseQuery.Where(t => t.Type == request.Type.Value);
            }

            if (request.Status.HasValue)
            {
                baseQuery = baseQuery.Where(t => t.Status == request.Status.Value);
            }

            var totalCount = await baseQuery.CountAsync(cancellationToken);

            var transactions = await baseQuery
                .OrderByDescending(t => t.CreatedAt)
                .ThenBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new UserTransactionDto
                {
                    Id = t.Id.ToString(),
                    UserPublicId = t.UserPublicId,
                    WalletId = t.WalletId,

                    Title = t.Title,
                    Provider = t.Provider != null ? t.Provider.ToString() : null,

                    Amount = new UserTransactionMoneyDto(t.Amount.ToDecimal(), t.Amount.Currency),

                    Type = t.Type.ToString(),
                    Status = t.Status.ToString(),

                    FailureReason = t.FailureReason,
                    Reference = t.Reference,
                    CompletedAt = t.CompletedAt,

                    CreatedAt = t.CreatedAt,
                    ModifiedAt = t.ModifiedAt,
                    IsDeleted = t.IsDeleted,
                    DeletedAt = t.DeletedAt,
                    CreatedBy = t.CreatedBy,
                    ModifiedBy = t.ModifiedBy,
                    DeletedBy = t.DeletedBy,
                })
                .ToListAsync(cancellationToken);

            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            return new BaseResult<BasePaginationResponse<UserTransactionDto>>
            (
                HttpStatusCode.OK,
                "User transactions retrieved successfully.",
                new BasePaginationResponse<UserTransactionDto>
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Items = transactions,
                }
            );
        }
    }
}

public sealed record UserTransactionMoneyDto(decimal Amount, string Currency);

public sealed record UserTransactionDto
{
    public string Id { get; init; } = string.Empty;
    public string UserPublicId { get; init; } = string.Empty;
    public long? WalletId { get; init; }

    public string? Title { get; init; }
    public string? Provider { get; init; }

    public UserTransactionMoneyDto Amount { get; init; } = new(0, "NGN");

    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    public string? FailureReason { get; init; }
    public string? Reference { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? ModifiedBy { get; init; }
    public string? DeletedBy { get; init; }
}