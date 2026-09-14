using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class GetReleasesQuery
{
    public sealed class Query : IRequest<BaseResult<GetReleasesQueryDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
        public int UpcomingLimit { get; set; } = 3;
    }

    public sealed class GetReleasesQueryDto
    {
        public List<ReleaseItem> TodayReleased { get; set; } = new();
        public List<ReleaseItem> Scheduled { get; set; } = new();
        public List<ReleaseItem> Upcoming { get; set; } = new();
    }

    public sealed class ReleaseItem
    {
        public long? ScheduledReleaseId { get; set; }

        public long WalletId { get; set; }

        public string WalletName { get; set; } = string.Empty;

        public string CategoryIcon { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTimeOffset ScheduledFor { get; set; }

        public DateTimeOffset? ReleasedAt { get; set; }

        public bool IsProjected { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<GetReleasesQueryDto>>
    {
        private const int MaximumProjectedReleases = 500;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly IWalletRuleService _walletRuleService;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            IWalletRuleService walletRuleService)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _walletRuleService = walletRuleService;
        }

        public async Task<BaseResult<GetReleasesQueryDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<GetReleasesQueryDto>(
                    HttpStatusCode.BadRequest,
                    "User public ID is required.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                return new BaseResult<GetReleasesQueryDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            try
            {
                var now = DateTimeOffset.UtcNow;

                var todayStart = new DateTimeOffset(
                    now.Year, now.Month, now.Day,
                    0, 0, 0, TimeSpan.Zero);

                var todayEnd = todayStart.AddDays(1);

                var upcomingLimit = request.UpcomingLimit > 0
                    ? request.UpcomingLimit
                    : 3;

                var wallets = await _unitOfWork.Query<Wallet>()
                    .AsNoTracking()
                    .Where(w => w.UserPublicId == request.UserPublicId)
                    .Include(w => w.Category)
                    .Select(w => new
                    {
                        w.Id,
                        w.Name,
                        CategoryIcon = w.Category != null ? w.Category.Icon : "FileText",
                        w.Status,
                        w.LockedAmount,
                        w.CreatedAt
                    })
                    .ToListAsync(cancellationToken);

                var walletLookup = wallets.ToDictionary(w => w.Id);


                var walletIds = wallets.Select(w => w.Id).ToList();

                var todayReleased = await _unitOfWork.Query<ScheduledRelease>()
                    .AsNoTracking()
                    .Where(sr =>
                        walletIds.Contains(sr.WalletId)
                        && sr.Status == ReleaseStatus.Released
                        && sr.ReleasedAt != null
                        && sr.ReleasedAt.Value >= todayStart
                        && sr.ReleasedAt.Value < todayEnd)
                    .OrderByDescending(sr => sr.ReleasedAt)
                    .ToListAsync(cancellationToken);

                var todayItems = todayReleased
                    .Select(sr =>
                    {
                        walletLookup.TryGetValue(sr.WalletId, out var w);
                        return new ReleaseItem
                        {
                            ScheduledReleaseId = sr.Id,
                            WalletId = sr.WalletId,
                            WalletName = w?.Name ?? string.Empty,
                            CategoryIcon = w?.CategoryIcon ?? "FileText",
                            Amount = sr.Amount.ToDecimal(),
                            Status = sr.Status.ToString(),
                            ScheduledFor = sr.ScheduledFor,
                            ReleasedAt = sr.ReleasedAt,
                            IsProjected = false
                        };
                    })
                    .ToList();

                var scheduledReleases = await _unitOfWork.Query<ScheduledRelease>()
                    .AsNoTracking()
                    .Where(sr =>
                        walletIds.Contains(sr.WalletId)
                        && sr.Status == ReleaseStatus.Scheduled)
                    .OrderBy(sr => sr.ScheduledFor)
                    .ThenBy(sr => sr.Id)
                    .ToListAsync(cancellationToken);

                var scheduledItems = scheduledReleases
                    .Select(sr =>
                    {
                        walletLookup.TryGetValue(sr.WalletId, out var w);
                        return new ReleaseItem
                        {
                            ScheduledReleaseId = sr.Id,
                            WalletId = sr.WalletId,
                            WalletName = w?.Name ?? string.Empty,
                            CategoryIcon = w?.CategoryIcon ?? "FileText",
                            Amount = sr.Amount.ToDecimal(),
                            Status = sr.Status.ToString(),
                            ScheduledFor = sr.ScheduledFor,
                            ReleasedAt = sr.ReleasedAt,
                            IsProjected = false
                        };
                    })
                    .ToList();


                var upcomingCandidates = new List<ReleaseItem>();

                var rules = await _unitOfWork.Query<WalletRule>()
                    .AsNoTracking()
                    .Where(r => walletIds.Contains(r.WalletId))
                    .ToListAsync(cancellationToken);

                var rulesByWallet = rules.ToDictionary(r => r.WalletId);
                var cursorByWallet = new Dictionary<long, DateTimeOffset>();

                foreach (var w in wallets)
                {
                    var latestKnown = scheduledReleases
                        .Where(sr => sr.WalletId == w.Id)
                        .Max(sr => (DateTimeOffset?)sr.ScheduledFor);

                    cursorByWallet[w.Id] = latestKnown ?? w.CreatedAt.AddTicks(-1);
                }

                foreach (var w in wallets)
                {
                    if (w.LockedAmount.MinorUnits <= 0)
                        continue;

                    if (!rulesByWallet.TryGetValue(w.Id, out var rule))
                        continue;

                    var scheduledAmountForWallet = scheduledReleases
                        .Where(sr => sr.WalletId == w.Id)
                        .Sum(sr => sr.Amount.MinorUnits);

                    var amountLeftToProject =
                        Math.Max(0, w.LockedAmount.MinorUnits - scheduledAmountForWallet);

                    if (amountLeftToProject <= 0)
                        continue;

                    var cursor = cursorByWallet[w.Id];

                    for (var i = 0; i < upcomingLimit && amountLeftToProject > 0; i++)
                    {
                        var next = await _walletRuleService.GetNextReleaseAsync(
                            rule, cursor, cancellationToken);

                        if (next is null)
                            break;

                        var amount = Math.Min(next.Amount.MinorUnits, amountLeftToProject);

                        if (amount <= 0)
                            break;

                        upcomingCandidates.Add(new ReleaseItem
                        {
                            ScheduledReleaseId = null,
                            WalletId = w.Id,
                            WalletName = w.Name,
                            CategoryIcon = w.CategoryIcon,
                            Amount = amount / 100m,
                            Status = ReleaseStatus.Scheduled.ToString(),
                            ScheduledFor = next.ScheduledFor,
                            ReleasedAt = null,
                            IsProjected = true
                        });

                        amountLeftToProject -= amount;
                        cursor = next.ScheduledFor;
                    }
                }

                var upcomingItems = upcomingCandidates
                    .OrderBy(x => x.ScheduledFor)
                    .ThenBy(x => x.WalletId)
                    .Take(upcomingLimit)
                    .ToList();

                var result = new GetReleasesQueryDto
                {
                    TodayReleased = todayItems,
                    Scheduled = scheduledItems,
                    Upcoming = upcomingItems
                };

                return new BaseResult<GetReleasesQueryDto>(
                    HttpStatusCode.OK,
                    "Releases retrieved successfully.",
                    result);
            }
            catch
            {
                return new BaseResult<GetReleasesQueryDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving releases. Please try again later.");
            }
        }
    }
}