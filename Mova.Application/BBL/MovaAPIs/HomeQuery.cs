using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class HomeQuery
{
    public sealed class Query : IRequest<BaseResult<HomeQueryDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public sealed class HomeQueryDto
    {
        public Balance Balance { get; set; } = new();
        public List<ReleasedSchedulesToday> TodayReleased { get; set; } = new();
        public List<Wallets> Wallets { get; set; } = new();
        public List<LockedAmountPoint> LockedAmountHistory { get; set; } = new();
    }

    public sealed class Balance
    {
        public decimal UserBalance { get; set; }
        public decimal TotalAvailableAmount { get; set; }
        public decimal TotalLockedAmount { get; set; }
    }

    public sealed class ReleasedSchedulesToday
    {
        public string WalletName { get; set; } = string.Empty;
        public decimal ReleasedAmount { get; set; }
        public DateTimeOffset ReleasedAt { get; set; }
    }

    public sealed class Wallets
    {
        public long Id { get; set; }
        public string WalletName { get; set; } = string.Empty;
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryIcon { get; set; } = string.Empty;
        public decimal TargetAmount { get; set; }
        public decimal ReleaseAmount { get; set; }
        public bool HasAutomation { get; set; }
        public string? AutomationStatus { get; set; }
    }

    public sealed class LockedAmountPoint
    {
        // e.g. "May 2026"
        public string Label { get; set; } = string.Empty;

        // Sum of target amounts for wallets created in this month
        public decimal Value { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<HomeQueryDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
        }

        public async Task<BaseResult<HomeQueryDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<HomeQueryDto>(
                    HttpStatusCode.BadRequest,
                    "User public ID is required.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                return new BaseResult<HomeQueryDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            try
            {
                var wallets = await _unitOfWork.Query<Wallet>()
                    .Where(w => w.UserPublicId == request.UserPublicId
                                && w.Status == WalletStatus.Active)
                    .Include(w => w.Category)
                    .Include(w => w.Rule)
                    .OrderByDescending(w => w.CreatedAt)
                    .Take(6)
                    .ToListAsync(cancellationToken);

                var walletIds = wallets.Select(w => w.Id).ToList();

                var policies = await _unitOfWork.Query<RenewalPolicy>()
                    .Where(p => walletIds.Contains(p.WalletId))
                    .Select(p => new
                    {
                        p.WalletId,
                        p.Status
                    })
                    .ToListAsync(cancellationToken);

                var policyByWalletId = policies.ToDictionary(p => p.WalletId);

                var today = DateTimeOffset.UtcNow.Date;

                var todayReleased = await (
                    from sr in _unitOfWork.Query<ScheduledRelease>()
                    join w in _unitOfWork.Query<Wallet>() on sr.WalletId equals w.Id
                    where sr.Status == ReleaseStatus.Released
                          && sr.ReleasedAt != null
                          && sr.ReleasedAt.Value.Date == today
                          && w.UserPublicId == request.UserPublicId
                          && w.Status == WalletStatus.Active
                    orderby sr.ReleasedAt descending
                    select new ReleasedSchedulesToday
                    {
                        WalletName = w.Name,
                        ReleasedAmount = sr.Amount.ToDecimal(),
                        ReleasedAt = sr.ReleasedAt ?? DateTimeOffset.UtcNow
                    })
                    .Take(5)
                    .ToListAsync(cancellationToken);

                var allWallets = await _unitOfWork.Query<Wallet>()
                    .Where(w => w.UserPublicId == request.UserPublicId
                                && w.Status == WalletStatus.Active)
                    .ToListAsync(cancellationToken);

                var totalAvailableAmount = allWallets.Sum(w => w.AvailableAmount.ToDecimal());
                var totalLockedAmount = allWallets.Sum(w => w.LockedAmount.ToDecimal());

                var walletSummaries = wallets.Select(w =>
                {
                    var hasPolicy = policyByWalletId.TryGetValue(w.Id, out var policy);

                    return new Wallets
                    {
                        Id = w.Id,
                        WalletName = w.Name,
                        CategoryId = w.CategoryId,
                        CategoryName = w.Category?.Name ?? "Other",
                        CategoryIcon = w.Category?.Icon ?? "FileText",
                        TargetAmount = w.TargetAmount.ToDecimal(),
                        ReleaseAmount = w.Rule?.Amount.ToDecimal() ?? 0m,
                        HasAutomation = hasPolicy,
                        AutomationStatus = hasPolicy ? policy!.Status.ToString() : null
                    };
                }).ToList();

                var lockedAmountHistory = await BuildLockedAmountHistoryAsync(
                    request.UserPublicId,
                    months: 5,
                    cancellationToken);

                var result = new HomeQueryDto
                {
                    Balance = new Balance
                    {
                        UserBalance = user.Balance.ToDecimal(),
                        TotalAvailableAmount = totalAvailableAmount,
                        TotalLockedAmount = totalLockedAmount
                    },
                    TodayReleased = todayReleased,
                    Wallets = walletSummaries,
                    LockedAmountHistory = lockedAmountHistory
                };

                return new BaseResult<HomeQueryDto>(
                    HttpStatusCode.OK,
                    "Home data retrieved successfully.",
                    result);
            }
            catch
            {
                return new BaseResult<HomeQueryDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving your home data. Please try again later.");
            }
        }

        /// <summary>
        /// For each of the last N months, returns the sum of TargetAmount
        /// for wallets created in that month.
        ///
        /// Example output for months = 5 on Sep 2026:
        ///   May 2026, Jun 2026, Jul 2026, Aug 2026, Sep 2026
        /// </summary>
        private async Task<List<LockedAmountPoint>> BuildLockedAmountHistoryAsync(
            string userPublicId,
            int months,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;

            var currentMonthStart = new DateTimeOffset(
                now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

            var windowStart = currentMonthStart.AddMonths(-(months - 1));
            var windowEnd = currentMonthStart.AddMonths(1);

            // Pull wallets created inside the window, with the fields we need
            // to compute the per-month sum of TargetAmount.
            var wallets = await _unitOfWork.Query<Wallet>()
                .Where(w => w.UserPublicId == userPublicId
                            && w.CreatedAt >= windowStart
                            && w.CreatedAt < windowEnd)
                .Select(w => new
                {
                    w.CreatedAt,
                    TargetMinorUnits = w.TargetAmount.MinorUnits
                })
                .ToListAsync(cancellationToken);

            // Group by the first day of the month the wallet was created in
            var byMonth = wallets
                .GroupBy(w => new DateTimeOffset(
                    w.CreatedAt.Year, w.CreatedAt.Month, 1, 0, 0, 0, TimeSpan.Zero))
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(x => x.TargetMinorUnits) / 100m);

            var result = new List<LockedAmountPoint>(months);

            for (var i = 0; i < months; i++)
            {
                var monthStart = windowStart.AddMonths(i);

                result.Add(new LockedAmountPoint
                {
                    Label = monthStart.ToString(
                        "MMM yyyy",
                        System.Globalization.CultureInfo.InvariantCulture),
                    Value = byMonth.TryGetValue(monthStart, out var v) ? v : 0m
                });
            }

            return result;
        }
    }
}