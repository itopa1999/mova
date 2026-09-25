using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetWalletActivities
{
    public sealed class Query : IRequest<BaseResult<WalletActivitiesResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; init; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class WalletActivitiesResponseDto : BasePaginationResponse<WalletActivityGroupDto>
    {
        public int TotalActivities { get; set; }
    }

    public sealed class WalletActivityGroupDto
    {
        public DateTime Date { get; set; }
        public List<WalletActivityDto> Activities { get; set; } = [];
    }

    public sealed class WalletActivityDto
    {
        public long Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public bool IsCredit { get; set; }
        public DateTimeOffset Date { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<WalletActivitiesResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<WalletActivitiesResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<WalletActivitiesResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User public ID is required.");
            }

            if (request.WalletId <= 0)
            {
                return new BaseResult<WalletActivitiesResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid wallet ID.");
            }

            var page = request.Page < 1 ? 1 : request.Page;
            var pageSize = request.PageSize < 1 ? 20 : Math.Min(request.PageSize, 100);

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId &&
                         x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                return new BaseResult<WalletActivitiesResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            var baseQuery = _unitOfWork.Query<Transaction>()
                .AsNoTracking()
                .Where(x =>
                    x.WalletId == wallet.Id &&
                    x.Status == TransactionStatus.Completed);

            var totalActivities = await baseQuery.CountAsync(cancellationToken);

            var pagedActivities = await baseQuery
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new WalletActivityDto
                {
                    Id = x.Id,
                    Type = x.Type.ToString(),
                    Title = x.Title,
                    Subtitle = x.Type.ToString(),
                    Amount = x.Amount.ToDecimal(),
                    IsCredit =
                        x.Type == TransactionType.Deposit ||
                        x.Type == TransactionType.Release ||
                        x.Type == TransactionType.Refund,
                    Date = x.CompletedAt ?? x.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var groupedActivities = pagedActivities
                .GroupBy(x => x.Date.Date)
                .OrderByDescending(x => x.Key)
                .Select(x => new WalletActivityGroupDto
                {
                    Date = x.Key,
                    Activities = x
                        .OrderByDescending(a => a.Date)
                        .ToList()
                })
                .ToList();

            var totalPages = (int)Math.Ceiling(
                (double)totalActivities / pageSize);

            var response = new WalletActivitiesResponseDto
            {
                TotalActivities = totalActivities,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalActivities,
                TotalPages = totalPages,
                Items = groupedActivities
            };

            return new BaseResult<WalletActivitiesResponseDto>(
                HttpStatusCode.OK,
                "Wallet activities retrieved successfully.",
                response);
        }
    }
}