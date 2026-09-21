using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetRenewalEventsQuery
{
    public sealed class Query : IRequest<BaseResult<GetRenewalEventsResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 20;
    }

    public sealed class RenewalEventDto
    {
        public long Id { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public string Result { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public decimal Amount { get; init; }
        public long? TransactionId { get; init; }
    }

    public sealed class GetRenewalEventsResponseDto
    {
        public long WalletId { get; init; }
        public string WalletName { get; init; } = string.Empty;

        public int Page { get; init; }
        public int PageSize { get; init; }
        public int TotalCount { get; init; }
        public int TotalPages { get; init; }

        public List<RenewalEventDto> Events { get; init; } = new();
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<GetRenewalEventsResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult<GetRenewalEventsResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "GetRenewalEvents",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId
                         && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Fail("Wallet not found.");
                return new BaseResult<GetRenewalEventsResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            var page = request.Page < 1 ? 1 : request.Page;
            var pageSize = request.PageSize < 1 ? 20 : request.PageSize;
            if (pageSize > 100) pageSize = 100;

            var baseQuery = _unitOfWork.Query<RenewalEvent>()
                .AsNoTracking()
                .Where(x => x.WalletId == wallet.Id);

            var totalCount = await baseQuery.CountAsync(cancellationToken);

            var events = await baseQuery
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new RenewalEventDto
                {
                    Id = x.Id,
                    OccurredAt = x.OccurredAt,
                    Result = x.Result.ToString(),
                    Reason = x.Reason,
                    Amount = x.Amount.ToDecimal(),
                    TransactionId = x.TransactionId,
                })
                .ToListAsync(cancellationToken);

            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            var dto = new GetRenewalEventsResponseDto
            {
                WalletId = wallet.Id,
                WalletName = wallet.Name,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Events = events,
            };

            op.Success(
                $"Renewal events returned. WalletId: {wallet.Id}, " +
                $"Total: {totalCount}, Returned: {events.Count}");

            return new BaseResult<GetRenewalEventsResponseDto>(
                HttpStatusCode.OK,
                "Automation history retrieved.",
                dto);
        }
    }
}