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
        public decimal TargetAmount { get; set; }
        public decimal LockedAmount { get; set; }
        public decimal ProgressPercentage { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Frequency { get; set; } = string.Empty;
        public string ScheduleDescription { get; set; } = string.Empty;
        public string NextRelease { get; set; } = string.Empty;
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
                    .Include(w => w.Rule)
                    .Include(w => w.ScheduledReleases)
                    .AsQueryable();

                var totalCount = await query.CountAsync(cancellationToken);

                var allWallets = await query.ToListAsync(cancellationToken);

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

                    var progressPercentage = w.TargetAmount.ToDecimal() > 0 
                        ? Math.Round((w.TotalReleasedAmount.ToDecimal() / w.TargetAmount.ToDecimal()) * 100, 2)
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
                        TargetAmount = w.TargetAmount.ToDecimal(),
                        LockedAmount = w.LockedAmount.ToDecimal(),
                        ProgressPercentage = progressPercentage,
                        Status = w.Status.ToString(),
                        Frequency = rule != null ? rule.Frequency.ToString() : "NotConfigured",
                        ScheduleDescription = scheduleDescription,
                        NextRelease = GetNextReleaseDisplay(nextReleaseDate)
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