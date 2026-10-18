using System.Net;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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

public sealed class WalletDetails
{
    public sealed class Query : IRequest<BaseResult<WalletDetailsResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long WalletId { get; set; }
    }

    public sealed class WalletDetailsResponseDto
    {
        // Identity
        public long WalletId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PayoutDestination { get; set; } = string.Empty;

        // Category
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryIcon { get; set; } = string.Empty;

        // Amounts
        public decimal TargetAmount { get; set; }
        public decimal LockedAmount { get; set; }
        public decimal TotalReleasedAmount { get; set; }
        public decimal AvailableAmount { get; set; }
        public decimal ResetAmount { get; set; }
        public decimal TotalWithdrawnAmount { get; set; }
        public decimal ProgressPercentage { get; set; }

        // Schedule
        public decimal ReleaseAmount { get; set; }
        public string Frequency { get; set; } = string.Empty;
        public string FrequencyConfig { get; set; } = string.Empty;
        public string ScheduleDescription { get; set; } = string.Empty;
        public DateTimeOffset StartDate { get; set; }
        public DateTimeOffset? EndDate { get; set; }

        // Release timing
        public DateTimeOffset? NextReleaseDate { get; set; }
        public string NextReleaseDisplay { get; set; } = string.Empty;
        public DateTimeOffset? LastReleaseDate { get; set; }
        public string LastReleaseDisplay { get; set; } = string.Empty;
        public DateTimeOffset? ProjectedEndDate { get; set; }
        public string ProjectedEndDateDisplay { get; set; } = string.Empty;

        // Summary + preview
        public ReleaseSummaryDto ReleaseSummary { get; set; } = new();
        public List<SchedulePreviewItemDto> SchedulePreview { get; set; } = new();

        // Automation
        public bool HasAutomation { get; set; }
        public string? AutomationStatus { get; set; }

        // Lifecycle counters
        public int RestartCount { get; set; }

        // Audit
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }

        // ─── Utilities ─────────────────────────────────────
        /// <summary>
        /// Present only when PayoutDestination == "utilities".
        /// One of: airtime | data | cable | electricity
        /// </summary>
        public string? UtilityType { get; set; }

        /// <summary>
        /// Structured utility config. Null when destination is not utilities.
        /// </summary>
        public UtilityConfigDto? UtilityConfig { get; set; }

        /// <summary>
        /// Human-friendly one-liner (e.g. "MTN 2GB Weekly to 08012345678").
        /// Null when destination is not utilities.
        /// </summary>
        public string? UtilitySummary { get; set; }
    }

    public sealed class UtilityConfigDto
    {
        public string UtilityType { get; set; } = string.Empty;

        // Airtime + Data
        public string? Network { get; set; }
        public string? PhoneNumber { get; set; }
        public string? PlanCode { get; set; }

        // Cable
        public string? CableProvider { get; set; }
        public string? SmartcardNumber { get; set; }
        public string? PackageCode { get; set; }

        // Electricity
        public string? Disco { get; set; }
        public string? MeterNumber { get; set; }
        public string? MeterType { get; set; }
    }

    public sealed class ReleaseSummaryDto
    {
        public int TotalReleases { get; set; }
        public int ReleasedCount { get; set; }
        public int ScheduledCount { get; set; }
        public int FailedCount { get; set; }
        public int ProjectedCount { get; set; }
        public int UpcomingReleases { get; set; }
        public decimal TotalReleasedAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public decimal AverageReleaseAmount { get; set; }
    }

    public sealed class SchedulePreviewItemDto
    {
        public long? ScheduledReleaseId { get; set; }
        public DateTimeOffset ScheduledFor { get; set; }
        public string ScheduledForDisplay { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsReleased { get; set; }
        public bool IsProjected { get; set; }
        public DateTimeOffset? ReleasedAt { get; set; }
        public string ReleasedAtDisplay { get; set; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<WalletDetailsResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly IMediator _mediator;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            IMediator mediator)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _mediator = mediator;
        }

        public async Task<BaseResult<WalletDetailsResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<WalletDetailsResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User public ID is required.");
            }

            if (request.WalletId <= 0)
            {
                return new BaseResult<WalletDetailsResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid wallet ID.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                return new BaseResult<WalletDetailsResponseDto>(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            try
            {
                var wallet = await _unitOfWork.Query<Wallet>()
                    .Where(w => w.Id == request.WalletId
                                && w.UserPublicId == request.UserPublicId
                                && w.Type == WalletType.Scheduled)
                    .Include(w => w.Rule)
                    .Include(w => w.Category)
                    .Include(w => w.ScheduledReleases)
                    .FirstOrDefaultAsync(cancellationToken);

                if (wallet == null)
                {
                    return new BaseResult<WalletDetailsResponseDto>(
                        HttpStatusCode.NotFound,
                        "Wallet not found.");
                }

                var renewalPolicy = await _unitOfWork.Query<RenewalPolicy>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.WalletId == wallet.Id, cancellationToken);

                var schedulePreviewResult = await _mediator.Send(
                    new GetWalletSchedulePreviewQuery.Query
                    {
                        UserPublicId = request.UserPublicId,
                        WalletId = request.WalletId
                    },
                    cancellationToken);

                var rule = wallet.Rule;
                var releases = wallet.ScheduledReleases ?? new List<ScheduledRelease>();

                var completedReleases = releases
                    .Where(r => r.Status == ReleaseStatus.Released)
                    .ToList();

                var scheduledReleases = releases
                    .Where(r => r.Status == ReleaseStatus.Scheduled)
                    .ToList();

                var failedReleases = releases
                    .Where(r => r.Status == ReleaseStatus.Failed)
                    .ToList();

                var projectedReleases = schedulePreviewResult.IsSuccess
                    && schedulePreviewResult.Data != null
                    ? schedulePreviewResult.Data.Releases.Count(r => r.IsProjected)
                    : 0;

                var totalReleasedAmount = completedReleases
                    .Sum(r => r.Amount.ToDecimal());

                var averageReleaseAmount = completedReleases.Any()
                    ? Math.Round(totalReleasedAmount / completedReleases.Count, 2)
                    : 0;

                var remainingAmount =
                    wallet.TargetAmount.ToDecimal() - totalReleasedAmount;

                var upcomingReleases =
                    scheduledReleases.Count + projectedReleases;

                var nextRelease = releases
                    .Where(r => r.Status == ReleaseStatus.Scheduled
                                && r.ScheduledFor > DateTimeOffset.UtcNow)
                    .OrderBy(r => r.ScheduledFor)
                    .FirstOrDefault();

                DateTimeOffset? nextReleaseDate = nextRelease?.ScheduledFor;

                if (nextReleaseDate is null
                    && schedulePreviewResult.IsSuccess
                    && schedulePreviewResult.Data != null)
                {
                    var firstProjected = schedulePreviewResult.Data.Releases
                        .Where(r => r.IsProjected)
                        .OrderBy(r => r.ScheduledFor)
                        .FirstOrDefault();

                    nextReleaseDate = firstProjected?.ScheduledFor;
                }

                var lastRelease = releases
                    .Where(r => r.Status == ReleaseStatus.Released
                                && r.ReleasedAt.HasValue)
                    .OrderByDescending(r => r.ReleasedAt)
                    .FirstOrDefault();

                DateTimeOffset? projectedEndDate = null;
                if (schedulePreviewResult.IsSuccess
                    && schedulePreviewResult.Data != null)
                {
                    var lastProjected = schedulePreviewResult.Data.Releases
                        .OrderByDescending(r => r.ScheduledFor)
                        .FirstOrDefault();

                    projectedEndDate = lastProjected?.ScheduledFor;
                }

                decimal cycleTotal;

                if (renewalPolicy is not null
                    && renewalPolicy.Status == RenewalStatus.Active
                    && renewalPolicy.TriggerType == RenewalTriggerType.OnCompletion)
                {
                    cycleTotal = renewalPolicy.RefillAmountType == RefillAmountType.Fixed
                        ? wallet.TargetAmount.ToDecimal()
                        : renewalPolicy.RefillAmount.ToDecimal();
                }
                else
                {
                    cycleTotal = wallet.TargetAmount.ToDecimal();
                }

                var progressPercentage = cycleTotal > 0
                    ? Math.Max(0, Math.Min(100, Math.Round(
                        (1 - (wallet.LockedAmount.ToDecimal() / cycleTotal)) * 100,
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

                var schedulePreview = new List<SchedulePreviewItemDto>();
                if (schedulePreviewResult.IsSuccess
                    && schedulePreviewResult.Data != null)
                {
                    schedulePreview = schedulePreviewResult.Data.Releases
                        .Select(r => new SchedulePreviewItemDto
                        {
                            ScheduledReleaseId = r.ScheduledReleaseId,
                            ScheduledFor = r.ScheduledFor,
                            ScheduledForDisplay = FormatDateDisplay(r.ScheduledFor),
                            Amount = r.Amount,
                            Status = r.Status,
                            IsReleased = r.IsReleased,
                            IsProjected = r.IsProjected,
                            ReleasedAt = r.ReleasedAt,
                            ReleasedAtDisplay = r.ReleasedAt.HasValue
                                ? FormatDateDisplay(r.ReleasedAt.Value)
                                : "Not released"
                        })
                        .ToList();
                }

                // ─── Utility config hydration ──────────────────
                UtilityConfigDto? utilityConfigDto = null;
                string? utilitySummary = null;

                if (wallet.PayoutDestination == PayoutDestination.Utilities
                    && wallet.UtilityType.HasValue)
                {
                    utilityConfigDto = DeserializeUtilityConfig(wallet.UtilityConfigJson);

                    if (utilityConfigDto is not null)
                    {
                        utilityConfigDto.UtilityType = wallet.UtilityType.Value
                            .ToString()
                            .ToLowerInvariant();

                        utilitySummary = BuildUtilitySummary(
                            wallet.UtilityType.Value,
                            utilityConfigDto);
                    }
                }

                var response = new WalletDetailsResponseDto
                {
                    WalletId = wallet.Id,
                    Name = wallet.Name,
                    Description = wallet.Description,
                    Status = wallet.Status.ToString(),
                    PayoutDestination = wallet.PayoutDestination
                        .ToString()
                        .ToLowerInvariant(),

                    CategoryId = wallet.CategoryId,
                    CategoryName = wallet.Category?.Name ?? "Other",
                    CategoryIcon = wallet.Category?.Icon ?? "FileText",

                    TargetAmount = wallet.TargetAmount.ToDecimal(),
                    LockedAmount = wallet.LockedAmount.ToDecimal(),
                    TotalReleasedAmount = wallet.TotalReleasedAmount.ToDecimal(),
                    AvailableAmount = wallet.AvailableAmount.ToDecimal(),
                    TotalWithdrawnAmount = wallet.TotalWithdrawnAmount.ToDecimal(),
                    ProgressPercentage = progressPercentage,

                    ResetAmount = wallet.ResetAmount.ToDecimal(),

                    ReleaseAmount = rule?.Amount.ToDecimal() ?? 0,
                    Frequency = rule != null
                        ? rule.Frequency.ToString()
                        : "NotConfigured",
                    FrequencyConfig = rule?.FrequencyConfig ?? string.Empty,
                    ScheduleDescription = scheduleDescription,
                    StartDate = rule != null
                        ? rule.StartDate
                        : DateTimeOffset.UtcNow,
                    EndDate = rule?.EndDate,

                    ReleaseSummary = new ReleaseSummaryDto
                    {
                        TotalReleases = releases.Count,
                        ReleasedCount = completedReleases.Count,
                        ScheduledCount = scheduledReleases.Count,
                        FailedCount = failedReleases.Count,
                        ProjectedCount = projectedReleases,
                        UpcomingReleases = upcomingReleases,
                        TotalReleasedAmount = totalReleasedAmount,
                        RemainingAmount = Math.Max(remainingAmount, 0),
                        AverageReleaseAmount = averageReleaseAmount,
                    },

                    NextReleaseDate = nextReleaseDate,
                    NextReleaseDisplay = GetNextReleaseDisplay(nextReleaseDate),
                    LastReleaseDate = lastRelease?.ReleasedAt,
                    LastReleaseDisplay = lastRelease?.ReleasedAt.HasValue == true
                        ? FormatDateDisplay(lastRelease.ReleasedAt.Value)
                        : "No releases yet",
                    ProjectedEndDate = projectedEndDate,
                    ProjectedEndDateDisplay = projectedEndDate.HasValue
                        ? FormatDateDisplay(projectedEndDate.Value)
                        : "Not available",

                    SchedulePreview = schedulePreview,

                    HasAutomation = renewalPolicy is not null,
                    AutomationStatus = renewalPolicy?.Status.ToString(),

                    RestartCount = wallet.RestartCount,

                    CreatedAt = wallet.CreatedAt,
                    UpdatedAt = wallet.ModifiedAt,

                    UtilityType = wallet.UtilityType?.ToString().ToLowerInvariant(),
                    UtilityConfig = utilityConfigDto,
                    UtilitySummary = utilitySummary,
                };

                return new BaseResult<WalletDetailsResponseDto>(
                    HttpStatusCode.OK,
                    "Wallet details retrieved successfully.",
                    response);
            }
            catch (Exception)
            {
                return new BaseResult<WalletDetailsResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving wallet details. Please try again later.");
            }
        }

        // ─────────────────────────────────────────────────────
        // Utility helpers
        // ─────────────────────────────────────────────────────

        private static UtilityConfigDto? DeserializeUtilityConfig(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var raw = JsonSerializer.Deserialize<UtilityConfig>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (raw is null)
                    return null;

                return new UtilityConfigDto
                {
                    UtilityType = raw.UtilityType ?? string.Empty,
                    Network = raw.Network,
                    PhoneNumber = raw.PhoneNumber,
                    PlanCode = raw.PlanCode,
                    CableProvider = raw.CableProvider,
                    SmartcardNumber = raw.SmartcardNumber,
                    PackageCode = raw.PackageCode,
                    Disco = raw.Disco,
                    MeterNumber = raw.MeterNumber,
                    MeterType = raw.MeterType,
                };
            }
            catch
            {
                return null;
            }
        }

        private static string BuildUtilitySummary(
            UtilityType utilityType,
            UtilityConfigDto config)
        {
            return utilityType switch
            {
                UtilityType.Airtime =>
                    $"{(config.Network ?? "Network").ToUpperInvariant()} airtime to {config.PhoneNumber}",

                UtilityType.Data =>
                    $"{(config.Network ?? "Network").ToUpperInvariant()} {config.PlanCode} to {config.PhoneNumber}",

                UtilityType.Cable =>
                    $"{config.CableProvider} {config.PackageCode} · ••••{Last4(config.SmartcardNumber)}",

                UtilityType.Electricity =>
                    $"{config.Disco} · ••••{Last4(config.MeterNumber)} ({config.MeterType})",

                _ => "Utility configured",
            };
        }

        private static string Last4(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= 4 ? value : value[^4..];
        }

        private static string GetNextReleaseDisplay(DateTimeOffset? nextReleaseDate)
        {
            if (nextReleaseDate == null)
                return "No upcoming releases";

            var now = DateTimeOffset.UtcNow;
            var daysUntil = (nextReleaseDate.Value - now).Days;

            if (daysUntil < 0) return "Overdue";
            if (daysUntil == 0) return "Today";
            if (daysUntil == 1) return "Tomorrow";
            if (daysUntil <= 7) return $"{daysUntil} days";
            if (daysUntil <= 14) return "Next week";
            if (daysUntil <= 30) return $"{daysUntil / 7} weeks";

            return nextReleaseDate.Value.ToString("MMM d, yyyy");
        }

        private static string FormatDateDisplay(DateTimeOffset date)
        {
            return date.ToString("MMM d, yyyy h:mm tt");
        }
    }
}