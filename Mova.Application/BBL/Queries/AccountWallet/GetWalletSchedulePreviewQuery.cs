using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWallet;

/// <summary>Returns the complete release timeline for one wallet, including projected future releases.</summary>
public sealed class GetWalletSchedulePreviewQuery
{
    public sealed class Query : IRequest<BaseResult<GetWalletSchedulePreviewResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; init; }
    }

    public sealed class GetWalletSchedulePreviewResponseDto
    {
        public long WalletId { get; init; }
        public decimal TargetAmount { get; init; }
        public decimal TotalReleasedAmount { get; init; }
        public decimal RemainingLockedAmount { get; init; }
        public List<ReleaseDto> Releases { get; set; } = new();
    }

    public sealed class ReleaseDto
    {
        public long? ScheduledReleaseId { get; init; }

        [JsonPropertyName("scheduled_for")]
        public DateTimeOffset ScheduledFor { get; init; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; init; }

        [JsonPropertyName("is_released")]
        public bool IsReleased { get; init; }

        [JsonPropertyName("released_at")]
        public DateTimeOffset? ReleasedAt { get; init; }

        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        // True when this row is calculated from the wallet rule and is not yet stored as a scheduled release.
        [JsonPropertyName("is_projected")]
        public bool IsProjected { get; init; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<GetWalletSchedulePreviewResponseDto>>
    {
        private const int MaximumProjectedReleases = 500;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IWalletRuleService _walletRuleService;

        public Handler(IUnitOfWork unitOfWork, IWalletRuleService walletRuleService)
        {
            _unitOfWork = unitOfWork;
            _walletRuleService = walletRuleService;
        }

        public async Task<BaseResult<GetWalletSchedulePreviewResponseDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<GetWalletSchedulePreviewResponseDto>(HttpStatusCode.BadRequest, "User ID is required.");
            }

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                return new BaseResult<GetWalletSchedulePreviewResponseDto>(HttpStatusCode.NotFound, "Wallet not found.");
            }

            var storedReleases = await _unitOfWork.Query<ScheduledRelease>()
                .AsNoTracking()
                .Where(x => x.WalletId == wallet.Id)
                .OrderBy(x => x.ScheduledFor)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

            var response = new GetWalletSchedulePreviewResponseDto
            {
                WalletId = wallet.Id,
                TargetAmount = wallet.TargetAmount.ToDecimal(),
                TotalReleasedAmount = wallet.TotalReleasedAmount.ToDecimal(),
                RemainingLockedAmount = wallet.LockedAmount.ToDecimal()
            };

            response.Releases.AddRange(storedReleases.Select(release => new ReleaseDto
            {
                ScheduledReleaseId = release.Id,
                ScheduledFor = release.ScheduledFor,
                Amount = release.Amount.ToDecimal(),
                IsReleased = release.Status == ReleaseStatus.Released,
                ReleasedAt = release.ReleasedAt,
                Status = release.Status.ToString(),
                IsProjected = false
            }));

            var rule = await _unitOfWork.Query<WalletRule>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.WalletId == wallet.Id, cancellationToken);

            if (rule is not null && wallet.LockedAmount.MinorUnits > 0)
            {
                // Amounts that already have a persisted, non-cancelled release must not be projected twice.
                var scheduledAmount = storedReleases
                    .Where(x => x.Status is ReleaseStatus.Scheduled or ReleaseStatus.Processing or ReleaseStatus.Failed)
                    .Sum(x => x.Amount.MinorUnits);
                var amountLeftToProject = Math.Max(0, wallet.LockedAmount.MinorUnits - scheduledAmount);
                var cursor = storedReleases
                    .Where(x => x.Status != ReleaseStatus.Cancelled)
                    .Select(x => (DateTimeOffset?)x.ScheduledFor)
                    .Max() ?? rule.StartDate.AddTicks(-1);

                for (var count = 0; amountLeftToProject > 0 && count < MaximumProjectedReleases; count++)
                {
                    var nextRelease = await _walletRuleService.GetNextReleaseAsync(rule, cursor, cancellationToken);
                    if (nextRelease is null)
                        break;

                    var amount = Math.Min(nextRelease.Amount.MinorUnits, amountLeftToProject);
                    if (amount <= 0)
                        break;

                    response.Releases.Add(new ReleaseDto
                    {
                        ScheduledFor = nextRelease.ScheduledFor,
                        Amount = amount / 100m,
                        IsReleased = false,
                        Status = ReleaseStatus.Scheduled.ToString(),
                        IsProjected = true
                    });

                    amountLeftToProject -= amount;
                    cursor = nextRelease.ScheduledFor;
                }
            }

            response.Releases = response.Releases
                .OrderBy(x => x.ScheduledFor)
                .ThenBy(x => x.ScheduledReleaseId ?? long.MaxValue)
                .ToList();

            return new BaseResult<GetWalletSchedulePreviewResponseDto>(HttpStatusCode.OK, "Wallet schedule preview retrieved successfully.", response);
        }
    }
}
