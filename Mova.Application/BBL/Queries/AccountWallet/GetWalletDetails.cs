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

public sealed class WalletDetails
{
    public sealed class Query : IRequest<BaseResult<WalletDetailsResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; set; }
    }

    public sealed class WalletDetailsResponseDto
    {
        // Wallet Information
        public long WalletId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Status { get; set; } = string.Empty;

        // Amounts
        public decimal TargetAmount { get; set; }
        public decimal LockedAmount { get; set; }
        public decimal ReleasedAmount { get; set; }
        public decimal TotalWithdrawnAmount { get; set; }
        public decimal AvailableAmount { get; set; }
        public decimal UnusedAmount { get; set; }
        public decimal ProgressPercentage { get; set; }
        public decimal SetAmountToBeRemoved {get; set;}

        // Schedule Information
        public string Frequency { get; set; } = string.Empty;
        public string ScheduleDescription { get; set; } = string.Empty;
        public DateTimeOffset StartDate { get; set; }
        public DateTimeOffset? EndDate { get; set; }

        // Release Information
        public ReleaseSummaryDto ReleaseSummary { get; set; } = new();
        public DateTimeOffset? NextReleaseDate { get; set; }
        public string NextReleaseDisplay { get; set; } = string.Empty;
        public DateTimeOffset? LastReleaseDate { get; set; }
        public string LastReleaseDisplay { get; set; } = string.Empty;
        public DateTimeOffset? ProjectedEndDate { get; set; }
        public string ProjectedEndDateDisplay { get; set; } = string.Empty;

        // Complete Schedule Preview (includes all releases - stored + projected)
        public List<SchedulePreviewItemDto> SchedulePreview { get; set; } = new();

        // Dates
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    public sealed class ReleaseSummaryDto
    {
        public int TotalReleases { get; set; }
        public int CompletedReleases { get; set; }
        public int ScheduledReleases { get; set; }
        public int FailedReleases { get; set; }
        public int ProjectedReleases { get; set; }
        public decimal TotalReleasedAmount { get; set; }
        public decimal AverageReleaseAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public decimal AllReleases{get; set; }
        public int RemainingReleases { get; set; }
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
                                && w.UserPublicId == request.UserPublicId)
                    .Include(w => w.Rule)
                    .Include(w => w.ScheduledReleases)
                    .FirstOrDefaultAsync(cancellationToken);

                if (wallet == null)
                {
                    return new BaseResult<WalletDetailsResponseDto>(
                        HttpStatusCode.NotFound,
                        "Wallet not found.");
                }

                // Get the schedule preview
                var schedulePreviewResult = await _mediator.Send(
                    new GetWalletSchedulePreviewQuery.Query
                    {
                        UserPublicId = request.UserPublicId,
                        WalletId = request.WalletId
                    },
                    cancellationToken);

                var rule = wallet.Rule;
                var SetAmountToBeRemoved = rule.Amount;
                var releases = wallet.ScheduledReleases ?? new List<ScheduledRelease>();

                // Calculate release summary
                var completedReleases = releases.Where(r => r.Status == ReleaseStatus.Released).ToList();
                var scheduledReleases = releases.Where(r => r.Status == ReleaseStatus.Scheduled).ToList();
                var failedReleases = releases.Where(r => r.Status == ReleaseStatus.Failed).ToList();

                // Get projected releases count from preview
                var projectedReleases = schedulePreviewResult.IsSuccess && schedulePreviewResult.Data != null
                    ? schedulePreviewResult.Data.Releases.Count(r => r.IsProjected)
                    : 0;

                var totalReleases = releases.Count;
                var totalReleasedAmount = completedReleases.Sum(r => r.Amount.ToDecimal());
                var averageReleaseAmount = completedReleases.Any() 
                    ? Math.Round(totalReleasedAmount / completedReleases.Count, 2) 
                    : 0;

                var remainingAmount = wallet.TargetAmount.ToDecimal() - totalReleasedAmount;
                var remainingReleases = projectedReleases;
                var AllReleases = scheduledReleases.Count + projectedReleases;

                // Get next release date
                var nextRelease = releases
                    .Where(r => r.Status == ReleaseStatus.Scheduled 
                                && r.ScheduledFor > DateTimeOffset.UtcNow)
                    .OrderBy(r => r.ScheduledFor)
                    .FirstOrDefault();

                // If no scheduled release, check projected releases
                if (nextRelease == null && schedulePreviewResult.IsSuccess && schedulePreviewResult.Data != null)
                {
                    var firstProjected = schedulePreviewResult.Data.Releases
                        .FirstOrDefault(r => r.IsProjected);
                    if (firstProjected != null)
                    {
                        nextRelease = new ScheduledRelease
                        {
                            ScheduledFor = firstProjected.ScheduledFor
                        };
                    }
                }

                // Get last release date
                var lastRelease = releases
                    .Where(r => r.Status == ReleaseStatus.Released && r.ReleasedAt.HasValue)
                    .OrderByDescending(r => r.ReleasedAt)
                    .FirstOrDefault();

                // Get projected end date from preview
                DateTimeOffset? projectedEndDate = null;
                if (schedulePreviewResult.IsSuccess && schedulePreviewResult.Data != null)
                {
                    var lastProjected = schedulePreviewResult.Data.Releases
                        .OrderByDescending(r => r.ScheduledFor)
                        .FirstOrDefault();
                    
                    if (lastProjected != null)
                    {
                        projectedEndDate = lastProjected.ScheduledFor;
                    }
                }

                // Calculate progress percentage
                var progressPercentage = wallet.TargetAmount.ToDecimal() > 0 
                    ? Math.Round((wallet.TotalReleasedAmount.ToDecimal() / wallet.TargetAmount.ToDecimal()) * 100, 2)
                    : 0;

                // Get schedule description
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

                // Build schedule preview from the query result
                var schedulePreview = new List<SchedulePreviewItemDto>();
                if (schedulePreviewResult.IsSuccess && schedulePreviewResult.Data != null)
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
                            ReleasedAtDisplay = r.ReleasedAt.HasValue ? FormatDateDisplay(r.ReleasedAt.Value) : "Not released"
                        })
                        .ToList();
                }

                var response = new WalletDetailsResponseDto
                {
                    // Wallet Information
                    WalletId = wallet.Id,
                    Name = wallet.Name,
                    Description = wallet.Description,
                    Status = wallet.Status.ToString(),

                    // Amounts
                    TargetAmount = wallet.TargetAmount.ToDecimal(),
                    LockedAmount = wallet.LockedAmount.ToDecimal(),
                    ReleasedAmount = wallet.TotalReleasedAmount.ToDecimal(),
                    TotalWithdrawnAmount = wallet.TotalWithdrawnAmount.ToDecimal(),
                    AvailableAmount = wallet.AvailableAmount.ToDecimal(),
                    UnusedAmount = wallet.UnusedAmount.ToDecimal(),
                    ProgressPercentage = progressPercentage,
                    SetAmountToBeRemoved = SetAmountToBeRemoved.ToDecimal(),

                    // Schedule Information
                    Frequency = rule != null ? rule.Frequency.ToString() : "NotConfigured",
                    ScheduleDescription = scheduleDescription,
                    StartDate = rule != null ? rule.StartDate : DateTimeOffset.UtcNow,
                    EndDate = rule?.EndDate,

                    // Release Summary
                    ReleaseSummary = new ReleaseSummaryDto
                    {
                        TotalReleases = totalReleases,
                        CompletedReleases = completedReleases.Count,
                        ScheduledReleases = scheduledReleases.Count,
                        FailedReleases = failedReleases.Count,
                        ProjectedReleases = projectedReleases,
                        TotalReleasedAmount = totalReleasedAmount,
                        AverageReleaseAmount = averageReleaseAmount,
                        RemainingAmount = Math.Max(remainingAmount, 0),
                        RemainingReleases = remainingReleases,
                        AllReleases = AllReleases
                    },

                    // Next and Last Release
                    NextReleaseDate = nextRelease?.ScheduledFor,
                    NextReleaseDisplay = GetNextReleaseDisplay(nextRelease?.ScheduledFor),
                    LastReleaseDate = lastRelease?.ReleasedAt,
                    LastReleaseDisplay = lastRelease?.ReleasedAt.HasValue == true 
                        ? FormatDateDisplay(lastRelease.ReleasedAt.Value) 
                        : "No releases yet",

                    // Projected End Date
                    ProjectedEndDate = projectedEndDate,
                    ProjectedEndDateDisplay = projectedEndDate.HasValue 
                        ? FormatDateDisplay(projectedEndDate.Value) 
                        : "Not available",

                    // Complete Schedule Preview
                    SchedulePreview = schedulePreview,

                    // Dates
                    CreatedAt = wallet.CreatedAt,
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

        private static string FormatDateDisplay(DateTimeOffset date)
        {
            return date.ToString("MMM d, yyyy h:mm tt");
        }
    }
}