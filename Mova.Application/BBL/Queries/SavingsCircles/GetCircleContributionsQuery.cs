using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Constants;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Queries.SavingsCircles;

public sealed class GetCircleContributionsQuery
{
    public sealed class Query : IRequest<BaseResult<GetCircleContributionsResponse>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;
        public long WalletId { get; set; }
        public string? TargetMemberUserPublicId { get; set; } 
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<GetCircleContributionsResponse>>
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICacheService _cacheService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork, 
            ICacheService cacheService,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<BaseResult<GetCircleContributionsResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
                return Bad("You must be signed in.", HttpStatusCode.Unauthorized);

            try
            {
                var wallet = await _unitOfWork.Query<Wallet>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.Id == request.WalletId && w.Type == WalletType.Circle, cancellationToken);

                if (wallet is null || wallet.SavingsCircleId is null)
                    return Bad("Circle wallet not found.", HttpStatusCode.NotFound);

                var circleId = wallet.SavingsCircleId.Value;

                var cacheKey = CacheKeys.CircleContributions(circleId, request.TargetMemberUserPublicId);

                var response = await _cacheService.GetOrSetAsync(
                    cacheKey,
                    ct => LoadContributionsFromDbAsync(
                        circleId,
                        wallet.Name,
                        request.TargetMemberUserPublicId,
                        ct),
                    timeout: CacheDuration,
                    cancellationToken: cancellationToken);

                return new BaseResult<GetCircleContributionsResponse>(
                    HttpStatusCode.OK,
                    "Contributions fetched successfully.",
                    response!);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching circle contributions for wallet {WalletId}", request.WalletId);
                return new BaseResult<GetCircleContributionsResponse>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving contributions. Please try again later.");
            }
        }

        private async Task<GetCircleContributionsResponse> LoadContributionsFromDbAsync(
            long circleId,
            string circleName,
            string? targetMemberUserPublicId,
            CancellationToken cancellationToken)
        {
            var query = _unitOfWork.Query<Contribution>()
                .AsNoTracking()
                .Include(c => c.CircleMember)
                .Include(c => c.CircleRound)
                .Where(c => c.CircleRound.SavingsCircleId == circleId);

            if (!string.IsNullOrWhiteSpace(targetMemberUserPublicId))
            {
                query = query.Where(c => c.CircleMember.UserPublicId == targetMemberUserPublicId);
            }

            var rawContributions = await query
                .OrderByDescending(c => c.CollectedAt ?? c.CreatedAt)
                .ToListAsync(cancellationToken);

            var groupedData = rawContributions
                .GroupBy(c => c.CollectedAt?.Date ?? c.CreatedAt.Date)
                .OrderByDescending(g => g.Key)
                .Select(g => new ContributionGroupDto
                {
                    Date = g.Key,
                    TotalCollected = g.Where(c => c.Status == ContributionStatus.Collected)
                                      .Sum(c => c.Amount.ToDecimal()),
                    TotalPenalties = g.Where(c => c.Status == ContributionStatus.Collected)
                                      .Sum(c => c.PenaltyApplied.ToDecimal()),
                    Items = g.Select(c => new ContributionItemDto
                    {
                        ContributionId = c.Id,
                        MemberName = c.CircleMember.DisplayName ?? "Unknown Member",
                        MemberUserPublicId = c.CircleMember.UserPublicId ?? string.Empty,
                        RoundNumber = c.CircleRound.RoundNumber,
                        Amount = c.Amount.ToDecimal(),
                        Penalty = c.PenaltyApplied.ToDecimal(),
                        Status = c.Status.ToString(),
                        CollectedAt = c.CollectedAt,
                        FailureReason = c.FailureReason
                    }).ToList()
                })
                .ToList();

            return new GetCircleContributionsResponse
            {
                CircleId = circleId,
                CircleName = circleName,
                TotalLifetimeCollected = groupedData.Sum(x => x.TotalCollected),
                TotalLifetimePenalties = groupedData.Sum(x => x.TotalPenalties),
                DailyGroups = groupedData
            };
        }

        private static BaseResult<GetCircleContributionsResponse> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);
    }
}

public sealed class GetCircleContributionsResponse
{
    public long CircleId { get; set; }
    public string CircleName { get; set; } = string.Empty;
    public decimal TotalLifetimeCollected { get; set; }
    public decimal TotalLifetimePenalties { get; set; }
    public List<ContributionGroupDto> DailyGroups { get; set; } = new();
}

public sealed class ContributionGroupDto
{
    public DateTime Date { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalPenalties { get; set; }
    public List<ContributionItemDto> Items { get; set; } = new();
}

public sealed class ContributionItemDto
{
    public long ContributionId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberUserPublicId { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public decimal Amount { get; set; }
    public decimal Penalty { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? CollectedAt { get; set; }
    public string? FailureReason { get; set; }
}