using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWalletTrustee;

public sealed class GetReleaseRequestsQuery
{
    public sealed class Query : IRequest<BaseResult<ReleaseRequestsPageDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long WalletId { get; set; }

        [Range(1, 100000)]
        public int Page { get; set; } = 1;

        [Range(1, 100)]
        public int PageSize { get; set; } = 20;

        [MaxLength(20)]
        public string? Status { get; set; }
    }

    public sealed class ReleaseRequestsPageDto
    {
        public long WalletId { get; init; }
        public int Page { get; init; }
        public int PageSize { get; init; }
        public int TotalCount { get; init; }
        public int TotalPages { get; init; }
        public int PendingCount { get; init; }
        public int ApprovedCount { get; init; }
        public int RejectedCount { get; init; }
        public int ExpiredCount { get; init; }
        public IReadOnlyList<ReleaseRequestItemDto> Items { get; init; }
            = Array.Empty<ReleaseRequestItemDto>();
    }

    public sealed class ReleaseRequestItemDto
    {
        public long Id { get; init; }
        public decimal Amount { get; init; }
        public string Destination { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset RequestedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset? DecidedAt { get; init; }
        public string? DecisionReason { get; init; }
        public string? UserNote { get; init; }
        public string? DecidedByTrusteeName { get; init; }
        public string? DecidedByTrusteeEmail { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<ReleaseRequestsPageDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<ReleaseRequestsPageDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.Id == request.WalletId &&
                         w.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                return new BaseResult<ReleaseRequestsPageDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Type != WalletType.Trustee)
            {
                return new BaseResult<ReleaseRequestsPageDto>(
                    HttpStatusCode.BadRequest,
                    "This is not a trustee wallet.");
            }

            var page = Math.Max(1, request.Page);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);

            var baseQuery = _unitOfWork.Query<ReleaseRequest>()
                .AsNoTracking()
                .Where(r => r.WalletId == wallet.Id);

            // Optional status filter
            if (!string.IsNullOrWhiteSpace(request.Status) &&
                Enum.TryParse<ReleaseRequestStatus>(
                    request.Status, true, out var statusFilter))
            {
                baseQuery = baseQuery.Where(r => r.Status == statusFilter);
            }

            // Status counts over the full set (not the filtered page)
            var statusGroups = await _unitOfWork.Query<ReleaseRequest>()
                .AsNoTracking()
                .Where(r => r.WalletId == wallet.Id)
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            int pending = 0, approved = 0, rejected = 0, expired = 0;

            foreach (var s in statusGroups)
            {
                switch (s.Status)
                {
                    case ReleaseRequestStatus.Pending: pending = s.Count; break;
                    case ReleaseRequestStatus.Approved: approved = s.Count; break;
                    case ReleaseRequestStatus.Rejected: rejected = s.Count; break;
                    case ReleaseRequestStatus.Expired: expired = s.Count; break;
                }
            }

            var totalCount = await baseQuery.CountAsync(cancellationToken);
            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            var items = await baseQuery
                .OrderByDescending(r => r.RequestedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id,
                    r.Amount,
                    r.Destination,
                    r.Status,
                    r.RequestedAt,
                    r.ExpiresAt,
                    r.DecidedAt,
                    r.DecisionReason,
                    r.UserNote,
                    r.DecidedByTrusteeId,
                })
                .ToListAsync(cancellationToken);

            var trusteeIds = items
                .Where(i => i.DecidedByTrusteeId != null)
                .Select(i => i.DecidedByTrusteeId!.Value)
                .Distinct()
                .ToList();

            var trusteeMap = trusteeIds.Count == 0
                ? new Dictionary<long, (string? Name, string Email)>()
                : await _unitOfWork.Query<WalletTrustee>()
                    .AsNoTracking()
                    .Where(t => trusteeIds.Contains(t.Id))
                    .Select(t => new { t.Id, t.Name, t.Email })
                    .ToDictionaryAsync(
                        t => t.Id,
                        t => ((string?)t.Name, t.Email),
                        cancellationToken);

            var dtoItems = items.Select(i =>
            {
                (string? Name, string Email) trusteeInfo = default;
                if (i.DecidedByTrusteeId is not null)
                {
                    trusteeMap.TryGetValue(i.DecidedByTrusteeId.Value, out trusteeInfo);
                }

                return new ReleaseRequestItemDto
                {
                    Id = i.Id,
                    Amount = i.Amount.MinorUnits / 100m,
                    Destination = i.Destination.ToString(),
                    Status = i.Status.ToString(),
                    RequestedAt = i.RequestedAt,
                    ExpiresAt = i.ExpiresAt,
                    DecidedAt = i.DecidedAt,
                    DecisionReason = i.DecisionReason,
                    UserNote = i.UserNote,
                    DecidedByTrusteeName = trusteeInfo.Name,
                    DecidedByTrusteeEmail = trusteeInfo.Email,
                };
            }).ToList();

            return new BaseResult<ReleaseRequestsPageDto>(
                HttpStatusCode.OK,
                "Release requests retrieved.",
                new ReleaseRequestsPageDto
                {
                    WalletId = wallet.Id,
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    PendingCount = pending,
                    ApprovedCount = approved,
                    RejectedCount = rejected,
                    ExpiredCount = expired,
                    Items = dtoItems,
                });
        }
    }
}