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

public sealed class GetTrusteeWalletDetailQuery
{
    public sealed class Query : IRequest<BaseResult<TrusteeWalletDetailDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long WalletId { get; set; }
    }

    public sealed class TrusteeWalletDetailDto
    {
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public long CategoryId { get; init; }
        public string CategoryName { get; init; } = string.Empty;
        public string CategoryIcon { get; init; } = string.Empty;
        public decimal TargetAmount { get; init; }
        public decimal LockedAmount { get; init; }
        public decimal TotalReleasedAmount { get; init; }
        public decimal AvailableAmount { get; init; }
        public decimal ProgressPercentage { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public DateTimeOffset? ClosedAt { get; init; }
        public TrusteeSummaryDto? Trustee { get; init; }
        public PendingReleaseSummaryDto? PendingRelease { get; init; }
        public ReleaseStatsDto Stats { get; init; } = new();
    }

    public sealed class TrusteeSummaryDto
    {
        public long Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string? Name { get; init; }
        public string Status { get; init; } = string.Empty;
        public DateTimeOffset? VerifiedAt { get; init; }
    }

    public sealed class PendingReleaseSummaryDto
    {
        public long Id { get; init; }
        public decimal Amount { get; init; }
        public DateTimeOffset RequestedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public string? UserNote { get; init; }
        public string PayoutDestination { get; init; } = string.Empty;
    }

    public sealed class ReleaseStatsDto
    {
        public int TotalRequests { get; init; }
        public int ApprovedCount { get; init; }
        public int RejectedCount { get; init; }
        public int ExpiredCount { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<TrusteeWalletDetailDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<TrusteeWalletDetailDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .Include(w => w.Category)
                .FirstOrDefaultAsync(
                    w => w.Id == request.WalletId &&
                         w.UserPublicId == request.UserPublicId
                         && w.Type == WalletType.Trustee,
                    cancellationToken);

            if (wallet is null)
            {
                return new BaseResult<TrusteeWalletDetailDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            TrusteeSummaryDto? trusteeDto = null;

            if (wallet.WalletTrusteeId is not null)
            {
                trusteeDto = await _unitOfWork.Query<WalletTrustee>()
                    .AsNoTracking()
                    .Where(t => t.Id == wallet.WalletTrusteeId.Value)
                    .Select(t => new TrusteeSummaryDto
                    {
                        Id = t.Id,
                        Email = t.Email,
                        Name = t.Name,
                        Status = t.Status.ToString(),
                    })
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var pendingRelease = await _unitOfWork.Query<ReleaseRequest>()
                .AsNoTracking()
                .Where(r =>
                    r.WalletId == wallet.Id &&
                    r.Status == ReleaseRequestStatus.Pending)
                .OrderByDescending(r => r.RequestedAt)
                .Select(r => new PendingReleaseSummaryDto
                {
                    Id = r.Id,
                    Amount = r.Amount.MinorUnits / 100m,
                    RequestedAt = r.RequestedAt,
                    ExpiresAt = r.ExpiresAt,
                    UserNote = r.UserNote,
                    PayoutDestination = r.Destination.ToString(),
                })
                .FirstOrDefaultAsync(cancellationToken);

            var statusGroups = await _unitOfWork.Query<ReleaseRequest>()
                .AsNoTracking()
                .Where(r => r.WalletId == wallet.Id)
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            int approved = 0, rejected = 0, expired = 0, total = 0;

            foreach (var s in statusGroups)
            {
                total += s.Count;

                switch (s.Status)
                {
                    case ReleaseRequestStatus.Approved:
                        approved = s.Count;
                        break;
                    case ReleaseRequestStatus.Rejected:
                        rejected = s.Count;
                        break;
                    case ReleaseRequestStatus.Expired:
                        expired = s.Count;
                        break;
                }
            }

            var targetAmount = wallet.TargetAmount.ToDecimal();
            var totalReleased = wallet.TotalReleasedAmount.ToDecimal();

            var progressPercentage = targetAmount > 0
                ? Math.Round((totalReleased / targetAmount) * 100, 2)
                : 0m;

            var result = new TrusteeWalletDetailDto
            {
                Id = wallet.Id,
                Name = wallet.Name,
                Description = wallet.Description,
                CategoryId = wallet.CategoryId,
                CategoryName = wallet.Category?.Name ?? "Other",
                CategoryIcon = wallet.Category?.Icon ?? "FileText",
                TargetAmount = targetAmount,
                LockedAmount = wallet.LockedAmount.ToDecimal(),
                TotalReleasedAmount = totalReleased,
                AvailableAmount = wallet.AvailableAmount.ToDecimal(),
                ProgressPercentage = progressPercentage,
                Status = wallet.Status.ToString(),
                Type = wallet.Type.ToString(),
                CreatedAt = wallet.CreatedAt,
                CompletedAt = wallet.CompletedAt,
                ClosedAt = wallet.ClosedAt,
                Trustee = trusteeDto,
                PendingRelease = pendingRelease,
                Stats = new ReleaseStatsDto
                {
                    TotalRequests = total,
                    ApprovedCount = approved,
                    RejectedCount = rejected,
                    ExpiredCount = expired,
                },
            };

            return new BaseResult<TrusteeWalletDetailDto>(
                HttpStatusCode.OK,
                "Trustee wallet retrieved.",
                result);
        }
    }
}