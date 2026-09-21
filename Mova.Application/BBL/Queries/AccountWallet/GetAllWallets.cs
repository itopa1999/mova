using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetAllWallets
{
    public sealed class Query : IRequest<BaseResult<GetAllWalletsResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// Optional. Case-insensitive search on wallet name and description.
        /// </summary>
        public string? Search { get; set; }
    }

    public sealed class GetAllWalletsResponseDto : BasePaginationResponse<WalletsDto>
    {
        public decimal TotalControlledAmount { get; set; }
        public int ActiveWalletCount { get; set; }
    }

    public sealed class WalletsDto
    {
        public long WalletId { get; init; }
        public string Name { get; set; } = string.Empty;
        public long CategoryId { get; init; }
        public string CategoryName { get; init; } = string.Empty;
        public string CategoryIcon { get; init; } = string.Empty;
        public decimal TargetAmount { get; set; }
        public decimal LockedAmount { get; set; }
        public decimal ProgressPercentage { get; set; }
        public decimal ReleaseAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Frequency { get; set; } = string.Empty;
        public string ScheduleDescription { get; set; } = string.Empty;
        public string NextRelease { get; set; } = string.Empty;
        public bool HasAutomation { get; set; }
        public string? AutomationStatus { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<GetAllWalletsResponseDto>>
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

        public async Task<BaseResult<GetAllWalletsResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<GetAllWalletsResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User public ID is required.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                return new BaseResult<GetAllWalletsResponseDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            try
            {
                var query = _unitOfWork.Query<Wallet>()
                    .Where(w => w.UserPublicId == request.UserPublicId)
                    .Include(w => w.Category)
                    .Include(w => w.Rule)
                    .Include(w => w.ScheduledReleases)
                    .AsQueryable();

                // ─── Search filter ────────────────────────────
                if (!string.IsNullOrWhiteSpace(request.Search))
                {
                    var term = request.Search.Trim().ToLowerInvariant();

                    // Filter the enum in memory first, then use Contains() — EF Core
                    // translates that to a simple "IN (...)" clause. Enum.ToString()
                    // cannot be translated to SQL.
                    var matchingStatuses = Enum.GetValues<WalletStatus>()
                        .Where(s => s.ToString().ToLowerInvariant().Contains(term))
                        .ToList();

                    query = query.Where(w =>
                        w.Name.ToLower().Contains(term) ||
                        (w.Description != null && w.Description.ToLower().Contains(term)) ||
                        (w.Category != null && w.Category.Name.ToLower().Contains(term)) ||
                        matchingStatuses.Contains(w.Status));
                }

                var totalCount = await query.CountAsync(cancellationToken);

                var allWallets = await query.ToListAsync(cancellationToken);

                // Totals reflect the filtered set, not the entire wallet list.
                var totalControlledAmount = allWallets.Sum(w => w.LockedAmount.ToDecimal());
                var activeWalletCount = allWallets.Count(w => w.Status == WalletStatus.Active);

                var sortedWallets = allWallets
                    .OrderByDescending(w => w.Status == WalletStatus.Active)
                    .ThenByDescending(w => w.CreatedAt)
                    .ToList();

                var pagedWallets = sortedWallets
                    .Skip((request.Page - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                var totalPages = (int)Math.Ceiling((double)totalCount / request.PageSize);

                // ─── Automation policies for the current page ─
                var pagedWalletIds = pagedWallets.Select(w => w.Id).ToList();

                var policies = await _unitOfWork.Query<RenewalPolicy>()
                    .Where(p => pagedWalletIds.Contains(p.WalletId))
                    .Select(p => new
                    {
                        p.WalletId,
                        p.Status,
                        p.IsEnabled,
                        p.TriggerType,
                        p.RefillAmountType,
                        p.RefillAmount
                    })
                    .ToListAsync(cancellationToken);

                var policyByWalletId = policies.ToDictionary(p => p.WalletId);

                var walletDtos = pagedWallets.Select(w =>
                {
                    var rule = w.Rule;

                    DateTimeOffset? nextReleaseDate = null;
                    if (rule != null)
                    {
                        var nextRelease = w.ScheduledReleases?
                            .Where(sr => sr.Status == ReleaseStatus.Scheduled
                                         && sr.ScheduledFor > DateTimeOffset.UtcNow)
                            .OrderBy(sr => sr.ScheduledFor)
                            .FirstOrDefault();

                        nextReleaseDate = nextRelease?.ScheduledFor;
                    }

                    // ─── Progress: per-cycle ──────────────────
                    var hasPolicy = policyByWalletId.TryGetValue(w.Id, out var policy);

                    decimal cycleTotal;

                    if (hasPolicy
                        && policy!.Status == RenewalStatus.Active
                        && policy.TriggerType == RenewalTriggerType.OnCompletion)
                    {
                        cycleTotal = policy.RefillAmountType == RefillAmountType.Fixed
                            ? w.TargetAmount.ToDecimal()
                            : policy.RefillAmount.ToDecimal();
                    }
                    else
                    {
                        cycleTotal = w.TargetAmount.ToDecimal();
                    }

                    var progressPercentage = cycleTotal > 0
                        ? Math.Max(0, Math.Min(100, Math.Round(
                            (1 - (w.LockedAmount.ToDecimal() / cycleTotal)) * 100,
                            2)))
                        : 0;

                    string scheduleDescription = string.Empty;
                    if (rule != null && !string.IsNullOrEmpty(rule.FrequencyConfig))
                    {
                        try
                        {
                            scheduleDescription = FrequencyConfigHelper.GetDescription(
                                rule.Frequency,
                                rule.FrequencyConfig);
                        }
                        catch
                        {
                            scheduleDescription = rule.Frequency.ToString();
                        }
                    }

                    return new WalletsDto
                    {
                        WalletId = w.Id,
                        Name = w.Name,
                        CategoryId = w.CategoryId,
                        CategoryName = w.Category?.Name ?? "Other",
                        CategoryIcon = w.Category?.Icon ?? "FileText",
                        TargetAmount = w.TargetAmount.ToDecimal(),
                        LockedAmount = w.LockedAmount.ToDecimal(),
                        ProgressPercentage = progressPercentage,
                        ReleaseAmount = rule?.Amount.ToDecimal() ?? 0m,
                        Status = w.Status.ToString(),
                        Frequency = rule != null ? rule.Frequency.ToString() : "NotConfigured",
                        ScheduleDescription = scheduleDescription,
                        NextRelease = GetNextReleaseDisplay(nextReleaseDate),
                        HasAutomation = hasPolicy,
                        AutomationStatus = hasPolicy ? policy!.Status.ToString() : null
                    };
                }).ToList();

                var response = new GetAllWalletsResponseDto
                {
                    TotalControlledAmount = totalControlledAmount,
                    ActiveWalletCount = activeWalletCount,
                    Page = request.Page,
                    PageSize = request.PageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Items = walletDtos
                };

                return new BaseResult<GetAllWalletsResponseDto>(
                    HttpStatusCode.OK,
                    "Wallets retrieved successfully.",
                    response);
            }
            catch (Exception)
            {
                return new BaseResult<GetAllWalletsResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving your wallets. Please try again later.");
            }
        }

        private static string GetNextReleaseDisplay(DateTimeOffset? nextReleaseDate)
        {
            if (nextReleaseDate == null)
                return "No upcoming releases";

            var now = DateTimeOffset.UtcNow;
            var daysUntil = (nextReleaseDate.Value - now).Days;

            if (daysUntil < 0)
                return "Overdue";

            if (daysUntil == 0)
                return "Today";

            if (daysUntil == 1)
                return "Tomorrow";

            if (daysUntil <= 7)
                return $"{daysUntil} days";

            if (daysUntil <= 14)
                return "Next week";

            if (daysUntil <= 30)
                return $"{daysUntil / 7} weeks";

            return nextReleaseDate.Value.ToString("MMM d, yyyy");
        }
    }
}