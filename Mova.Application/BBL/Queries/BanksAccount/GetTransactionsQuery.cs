using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.BanksAccount;

public sealed class GetTransactions
{
    public sealed class Query : IRequest<BaseResult<PaginatedTransactionsDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
        public int Page { get; set; } = 1;

        /// <summary>Items per page. Capped at 100 server-side.</summary>
        public int PageSize { get; set; } = 20;

        /// <summary>Optional. Filter by transaction type (Deposit, Release, Fee, ...).</summary>
        public TransactionType? Type { get; set; }

        /// <summary>Optional. Filter by status (Pending, Completed, Failed, ...).</summary>
        public TransactionStatus? Status { get; set; }

        /// <summary>Optional. Filter by wallet. Use 0 or null for "all wallets".</summary>
        public long? WalletId { get; set; }

        /// <summary>Optional. Filter by provider (Paystack, Monnify, Flutterwave).</summary>
        public PaymentProvider? Provider { get; set; }

        /// <summary>Optional. Only return transactions created on or after this date.</summary>
        public DateTimeOffset? FromDate { get; set; }

        /// <summary>Optional. Only return transactions created on or before this date.</summary>
        public DateTimeOffset? ToDate { get; set; }

        /// <summary>Optional. Case-insensitive search on title and reference.</summary>
        public string? Search { get; set; }
    }

    public sealed class PaginatedTransactionsDto
    {
        public List<TransactionDto> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }
    }

    public sealed class TransactionDto
    {
        public long Id { get; set; }
        public string? Title { get; set; }
        public decimal Amount { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Provider { get; set; }
        public string? Reference { get; set; }
        public string? FailureReason { get; set; }
        public long? WalletId { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<PaginatedTransactionsDto>>
    {
        private const int MaxPageSize = 100;

        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<PaginatedTransactionsDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            // ─── Normalize pagination ─────────────────────────────
            var page = request.Page < 1 ? 1 : request.Page;
            var pageSize = request.PageSize < 1 ? 20 : request.PageSize;
            if (pageSize > MaxPageSize) pageSize = MaxPageSize;

            // ─── Base query: user-scoped, not deleted ─────────────
            var query = _unitOfWork.Query<Transaction>()
                .AsNoTracking()
                .Where(x => x.UserPublicId == request.UserPublicId);

            // ─── Filters ──────────────────────────────────────────
            if (request.Type.HasValue)
            {
                query = query.Where(x => x.Type == request.Type.Value);
            }

            if (request.Status.HasValue)
            {
                query = query.Where(x => x.Status == request.Status.Value);
            }

            if (request.WalletId.HasValue && request.WalletId.Value > 0)
            {
                query = query.Where(x => x.WalletId == request.WalletId.Value);
            }

            if (request.Provider.HasValue)
            {
                query = query.Where(x => x.Provider == request.Provider.Value);
            }

            if (request.FromDate.HasValue)
            {
                var from = request.FromDate.Value.ToUniversalTime();
                query = query.Where(x => x.CreatedAt >= from);
            }

            if (request.ToDate.HasValue)
            {
                var to = request.ToDate.Value.ToUniversalTime();
                query = query.Where(x => x.CreatedAt <= to);
            }

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var term = request.Search.Trim().ToLowerInvariant();
                query = query.Where(x =>
                    (x.Title != null && x.Title.ToLower().Contains(term)) ||
                    (x.Reference != null && x.Reference.ToLower().Contains(term)));
            }

            // ─── Total + page ─────────────────────────────────────
            var totalItems = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new TransactionDto
                {
                    Id = x.Id,
                    Title = x.Title,
                    Amount = x.Amount.ToDecimal(),
                    Type = x.Type.ToString(),
                    Status = x.Status.ToString(),
                    Provider = x.Provider.HasValue
                        ? x.Provider.Value.ToString()
                        : null,
                    Reference = x.Reference,
                    FailureReason = x.FailureReason,
                    WalletId = x.WalletId,
                    CompletedAt = x.CompletedAt,
                    CreatedAt = x.CreatedAt,
                })
                .ToListAsync(cancellationToken);

            var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            return new BaseResult<PaginatedTransactionsDto>(
                HttpStatusCode.OK,
                "Transactions retrieved successfully.",
                new PaginatedTransactionsDto
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalItems = totalItems,
                    TotalPages = totalPages,
                    HasNextPage = page < totalPages,
                    HasPreviousPage = page > 1,
                });
        }
    }
}