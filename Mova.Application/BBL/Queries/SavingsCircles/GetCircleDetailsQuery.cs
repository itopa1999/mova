using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Queries.SavingsCircles;

public sealed class GetCircleDetailsQuery
{
    public sealed class Query : IRequest<BaseResult<CircleDetailsResponseDto>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;

        [Required]
        public long WalletId { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<CircleDetailsResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly ISavingsCircleService _savingsCircleService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            ISavingsCircleService savingsCircleService,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _savingsCircleService = savingsCircleService;
            _logger = logger;
        }

        public async Task<BaseResult<CircleDetailsResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "GetCircleDetails",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            // ── 1. Load wallet, verify ownership + type ────────────────
            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.Id == request.WalletId &&
                         w.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Warning("Wallet not found or not owned by user.");
                return new BaseResult<CircleDetailsResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Type != WalletType.Circle || wallet.SavingsCircleId is null)
            {
                op.Warning("Wallet is not a circle wallet.");
                return new BaseResult<CircleDetailsResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet is not linked to a savings circle.");
            }

            // ── 2. Load circle + members ───────────────────────────────
            var circle = await _unitOfWork.Query<SavingsCircle>()
                .AsNoTracking()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(
                    c => c.Id == wallet.SavingsCircleId.Value,
                    cancellationToken);

            if (circle is null)
            {
                op.Warning("Circle not found for wallet.");
                return new BaseResult<CircleDetailsResponseDto>(
                    HttpStatusCode.NotFound,
                    "The circle linked to this wallet no longer exists.");
            }

            // ── 3. Load category ───────────────────────────────────────
            var category = await _unitOfWork.Query<WalletCategory>()
                .AsNoTracking()
                .Where(c => c.Id == wallet.CategoryId)
                .Select(c => new { c.Id, c.Name, c.Icon })
                .FirstOrDefaultAsync(cancellationToken);

            // ── 4. Load rounds (source of truth for payout history) ───
            var rounds = await _unitOfWork.Query<CircleRound>()
                .AsNoTracking()
                .Where(r => r.SavingsCircleId == circle.Id)
                .OrderBy(r => r.RoundNumber)
                .ToListAsync(cancellationToken);

            var paidRounds = rounds
                .Where(r => r.Status == CircleRoundStatus.PaidOut)
                .ToList();

            var currentRoundEntity = rounds
                .Where(r =>
                    r.Status == CircleRoundStatus.Pending ||
                    r.Status == CircleRoundStatus.Collecting)
                .OrderByDescending(r => r.RoundNumber)
                .FirstOrDefault();

            // ── 5. Locate caller's member row ──────────────────────────
            var currentMember = circle.Members.FirstOrDefault(m =>
                !string.IsNullOrWhiteSpace(m.UserPublicId) &&
                m.UserPublicId == request.UserPublicId);

            if (currentMember is null)
            {
                op.Warning("User is not a member of this circle.");
                return new BaseResult<CircleDetailsResponseDto>(
                    HttpStatusCode.Forbidden,
                    "You are not a member of this circle.");
            }

            // ── 6. Resolve organizer name ──────────────────────────────
            var organizer = await _identityService.GetByIdentifierAsync(
                circle.OrganizerUserPublicId, cancellationToken);
            var organizerName = organizer is not null
                ? $"{organizer.FirstName} {organizer.LastName}".Trim()
                : "Organizer";
            if (string.IsNullOrWhiteSpace(organizerName))
                organizerName = "Organizer";

            // ── 7. Compute aggregates ──────────────────────────────────
            var activeMembers = circle.Members
                .Where(m => m.Status == CircleMemberStatus.Active)
                .ToList();
            var activeCount = activeMembers.Count;
            var slotsRemaining = Math.Max(0, circle.MemberCount - activeCount);
            var perRoundDue = circle.ContributionAmount.ToDecimal();

            // ── 8. Current round (prefer an in-flight round row) ───────
            int currentRound;
            if (circle.Status == SavingsCircleStatus.Draft)
            {
                currentRound = 0;
                currentRoundEntity = null;
            }
            else if (circle.Status == SavingsCircleStatus.Completed)
            {
                currentRound = circle.MemberCount;
            }
            else if (currentRoundEntity is not null)
            {
                currentRound = currentRoundEntity.RoundNumber;
            }
            else
            {
                currentRound = Math.Min(circle.MemberCount, paidRounds.Count + 1);
            }

            // ── 9. Has the caller already received a payout? ───────────
            var hasReceivedPayout = paidRounds.Any(r =>
                !string.IsNullOrWhiteSpace(r.RecipientUserPublicId) &&
                r.RecipientUserPublicId == request.UserPublicId);

            if (!hasReceivedPayout && currentMember.PayoutReceivedAt is not null)
                hasReceivedPayout = true;

            // ── 10. Current round recipient (entity first, rotation fallback) ──
            string? currentRecipientPublicId =
                currentRoundEntity?.RecipientUserPublicId;

            if (string.IsNullOrWhiteSpace(currentRecipientPublicId) &&
                currentRound > 0 &&
                circle.Status == SavingsCircleStatus.Active &&
                circle.RotationStrategy != RotationStrategy.RandomDraw)
            {
                var rotated = activeMembers
                    .FirstOrDefault(m => m.RotationPosition == currentRound);
                currentRecipientPublicId = rotated?.UserPublicId;
            }

            CircleCurrentRecipientDto? currentRecipient = null;
            if (!string.IsNullOrWhiteSpace(currentRecipientPublicId))
            {
                var recipientMember = circle.Members.FirstOrDefault(m =>
                    m.UserPublicId == currentRecipientPublicId);
                if (recipientMember is not null)
                {
                    currentRecipient = new CircleCurrentRecipientDto
                    {
                        MemberId = recipientMember.Id,
                        DisplayName = recipientMember.DisplayName ?? "Member",
                        RotationPosition = recipientMember.RotationPosition
                    };
                }
            }

            var isRecipientThisRound =
                !string.IsNullOrWhiteSpace(currentRecipientPublicId) &&
                currentRecipientPublicId == request.UserPublicId;

            // ── 11. Next recipient + rounds until my turn ──────────────
            var isNextRecipient = false;
            int? roundsUntilMyTurn = null;

            if (currentMember.Status == CircleMemberStatus.Active &&
                currentMember.RotationPosition is int myPosition &&
                currentRound > 0 &&
                circle.Status == SavingsCircleStatus.Active)
            {
                if (myPosition == currentRound)
                {
                    roundsUntilMyTurn = 0;
                }
                else if (myPosition > currentRound)
                {
                    roundsUntilMyTurn = myPosition - currentRound;
                    isNextRecipient = roundsUntilMyTurn == 1;
                }
            }

            // ── 12. Round progress — prefer Contribution rows, fall back ──
            bool hasPaidThisRound = false;
            var paidThisRoundCount = 0;
            var roundCollected = 0m;

            if (currentRoundEntity is not null)
            {
                var paidContributions = await _unitOfWork.Query<Contribution>()
                    .AsNoTracking()
                    .Where(c =>
                        c.CircleRoundId == currentRoundEntity.Id &&
                        c.Status == ContributionStatus.Collected)
                    .ToListAsync(cancellationToken);

                paidThisRoundCount = paidContributions.Count;
                roundCollected = paidContributions.Sum(c => c.Amount.ToDecimal());
                hasPaidThisRound = paidContributions
                    .Any(c => c.CircleMemberId == currentMember.Id);
            }
            else if (circle.Status == SavingsCircleStatus.Active &&
                currentRound > 0 &&
                currentMember.Status == CircleMemberStatus.Active)
            {
                hasPaidThisRound =
                    currentMember.TotalContributed.ToDecimal() >=
                    perRoundDue * currentRound;

                paidThisRoundCount = activeMembers.Count(m =>
                    m.TotalContributed.ToDecimal() >=
                    perRoundDue * currentRound);

                roundCollected = paidThisRoundCount * perRoundDue;
            }

            // ── 13. Next collection / payout dates ─────────────────────
            var nextCollectionDate =
                currentRoundEntity?.CollectionDate ??
                circle.NextCollectionDate;

            var nextPayoutDate =
                currentRoundEntity?.PayoutDate ??
                circle.NextPayoutDate;

            // ── 14. Access state ───────────────────────────────────────
            var isOrganizer =
                currentMember.UserPublicId == circle.OrganizerUserPublicId;

            var canContribute =
                circle.Status == SavingsCircleStatus.Active &&
                currentMember.Status == CircleMemberStatus.Active &&
                !hasPaidThisRound;

            var requiredMembersToStart = circle.StartPolicy == StartPolicy.WaitForAll
                ? circle.MemberCount
                : 3;

            var canStartCircle =
                isOrganizer &&
                circle.Status == SavingsCircleStatus.Draft &&
                circle.ModeratorStartsCircle &&
                activeCount >= requiredMembersToStart &&
                DateTimeOffset.UtcNow >= circle.StartDate;

            var canManageMembers =
                isOrganizer &&
                circle.Status == SavingsCircleStatus.Draft;

            var canCancelCircle =
                isOrganizer &&
                circle.Status != SavingsCircleStatus.Completed &&
                circle.Status != SavingsCircleStatus.Cancelled;

            var myContributionStatus = currentMember.Status switch
            {
                CircleMemberStatus.Invited => "NotStarted",
                CircleMemberStatus.Active => hasPaidThisRound ? "Paid" : "Pending",
                CircleMemberStatus.Defaulted => "Defaulted",
                CircleMemberStatus.Exited => "NotStarted",
                CircleMemberStatus.Completed => "Paid",
                _ => "NotStarted"
            };

            // ── 15. Frequency display ──────────────────────────────────
            var frequencyDisplay = _savingsCircleService.HumanizeFrequency(
                circle.Frequency,
                circle.FrequencyConfig ?? string.Empty);

            // ── 16. Build response ─────────────────────────────────────
            var dto = new CircleDetailsResponseDto
            {
                AccessState = new CircleAccessStateDto
                {
                    IsOrganizer = isOrganizer,
                    IsActiveMember = currentMember.Status == CircleMemberStatus.Active,
                    HasReceivedPayout = hasReceivedPayout,
                    HasPaidThisRound = hasPaidThisRound,
                    IsRecipientThisRound = isRecipientThisRound,
                    IsNextRecipient = isNextRecipient,
                    CanContribute = canContribute,
                    CanStartCircle = canStartCircle,
                    CanManageMembers = canManageMembers,
                    CanCancelCircle = canCancelCircle
                },

                CircleDetails = new CircleInfoDto
                {
                    CircleId = circle.Id,
                    WalletId = wallet.Id,
                    Name = circle.Name,
                    Description = circle.Description ?? string.Empty,
                    Status = circle.Status.ToString(),
                    OrganizerPublicId = circle.OrganizerUserPublicId,
                    OrganizerName = organizerName,
                    CategoryId = category?.Id,
                    CategoryName = category?.Name,
                    CategoryIcon = category?.Icon,
                    CreatedAt = circle.CreatedAt,
                    StartedAt = circle.Status != SavingsCircleStatus.Draft
                        ? circle.StartDate
                        : null,
                    CompletedAt = circle.CompletedAt
                },

                MyMembership = new CircleMyMembershipDto
                {
                    MemberId = currentMember.Id,
                    DisplayName = currentMember.DisplayName ?? "Member",
                    Status = currentMember.Status.ToString(),
                    RotationPosition = currentMember.RotationPosition,
                    JoinedAt = currentMember.InvitedAt,
                    AcceptedAt = currentMember.AcceptedAt,
                    TotalContributed = currentMember.TotalContributed.ToDecimal(),
                    TotalReceived = currentMember.TotalReceived.ToDecimal(),
                    DepositHeld = currentMember.DepositHeld.ToDecimal(),
                    MissedContributions = currentMember.MissedContributions,
                    PayoutReceivedAt = currentMember.PayoutReceivedAt,
                    ContributionStatusThisRound = myContributionStatus,
                    IsRecipientThisRound = isRecipientThisRound,
                    IsNextRecipient = isNextRecipient,
                    RoundsUntilMyTurn = roundsUntilMyTurn
                },

                MyWallet = new CircleMyWalletDto
                {
                    WalletId = wallet.Id,
                    AvailableAmount = wallet.AvailableAmount.ToDecimal(),
                    LockedAmount = wallet.LockedAmount.ToDecimal(),
                    TargetAmount = wallet.TargetAmount.ToDecimal(),
                    FundedAmount = wallet.FundedAmount.ToDecimal(),
                    TotalReleasedAmount = wallet.TotalReleasedAmount.ToDecimal(),
                    TotalWithdrawnAmount = wallet.TotalWithdrawnAmount.ToDecimal()
                },

                FinancialStats = new CircleFinancialStatsDto
                {
                    ContributionAmount = perRoundDue,
                    RoundPool = perRoundDue * circle.MemberCount,
                    DepositAmount = circle.DepositAmount.ToDecimal(),
                    TotalDepositsHeld = circle.Members.Sum(m => m.DepositHeld.ToDecimal()),
                    TotalContributedAmount = circle.Members.Sum(m => m.TotalContributed.ToDecimal())
                },

                RoundInfo = new CircleRoundInfoDto
                {
                    CurrentRound = currentRound,
                    TotalRounds = circle.MemberCount,
                    RoundCollected = roundCollected,
                    RoundCollectedCount = paidThisRoundCount,
                    NextCollectionDate = nextCollectionDate,
                    NextPayoutDate = nextPayoutDate,
                    CurrentRecipient = currentRecipient
                },

                RulesAndSchedule = new CircleRulesDto
                {
                    Frequency = circle.Frequency.ToString(),
                    FrequencyDisplay = frequencyDisplay,
                    PenaltyAmount = circle.PenaltyAmount.ToDecimal(),
                    PenaltySource = circle.PenaltyAmountFrom.ToString(),
                    GracePeriodHours = circle.GracePeriodHours,
                    RotationStrategy = circle.RotationStrategy.ToString(),
                    StartDate = circle.StartDate,
                    StartPolicy = circle.StartPolicy.ToString(),
                    ModeratorStartsCircle = circle.ModeratorStartsCircle
                },

                MemberStats = new CircleMemberStatsDto
                {
                    MemberCount = circle.MemberCount,
                    ActiveMemberCount = activeCount,
                    SlotsRemaining = slotsRemaining,
                    PaidThisRound = paidThisRoundCount
                },

                InviteInfo = new CircleInviteInfoDto
                {
                    InviteCode = circle.InviteCode ?? string.Empty,
                    InviteOnly = circle.InviteOnly
                }
            };

            op.Success(
                $"Circle details retrieved. CircleId: {circle.Id}, " +
                $"WalletId: {wallet.Id}, Round: {currentRound}/{circle.MemberCount}, " +
                $"PaidRounds: {paidRounds.Count}, Status: {circle.Status}.");

            return new BaseResult<CircleDetailsResponseDto>(
                HttpStatusCode.OK,
                "Circle details retrieved successfully.",
                dto);
        }
    }
}

/* ───────────── Response DTOs ───────────── */

public sealed class CircleDetailsResponseDto
{
    public CircleAccessStateDto AccessState { get; set; } = new();
    public CircleInfoDto CircleDetails { get; set; } = new();
    public CircleMyMembershipDto MyMembership { get; set; } = new();
    public CircleMyWalletDto MyWallet { get; set; } = new();
    public CircleFinancialStatsDto FinancialStats { get; set; } = new();
    public CircleRoundInfoDto RoundInfo { get; set; } = new();
    public CircleRulesDto RulesAndSchedule { get; set; } = new();
    public CircleMemberStatsDto MemberStats { get; set; } = new();
    public CircleInviteInfoDto InviteInfo { get; set; } = new();
}

public sealed class CircleAccessStateDto
{
    public bool IsOrganizer { get; set; }
    public bool IsActiveMember { get; set; }
    public bool HasReceivedPayout { get; set; }
    public bool HasPaidThisRound { get; set; }
    public bool IsRecipientThisRound { get; set; }
    public bool IsNextRecipient { get; set; }
    public bool CanContribute { get; set; }
    public bool CanStartCircle { get; set; }
    public bool CanManageMembers { get; set; }
    public bool CanCancelCircle { get; set; }
}

public sealed class CircleInfoDto
{
    public long CircleId { get; set; }
    public long WalletId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OrganizerPublicId { get; set; } = string.Empty;
    public string OrganizerName { get; set; } = string.Empty;
    public long? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryIcon { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class CircleMyMembershipDto
{
    public long MemberId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? RotationPosition { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public decimal TotalContributed { get; set; }
    public decimal TotalReceived { get; set; }
    public decimal DepositHeld { get; set; }
    public int MissedContributions { get; set; }
    public DateTimeOffset? PayoutReceivedAt { get; set; }
    public string ContributionStatusThisRound { get; set; } = string.Empty;
    public bool IsRecipientThisRound { get; set; }
    public bool IsNextRecipient { get; set; }
    public int? RoundsUntilMyTurn { get; set; }
}

public sealed class CircleMyWalletDto
{
    public long WalletId { get; set; }
    public decimal AvailableAmount { get; set; }
    public decimal LockedAmount { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal FundedAmount { get; set; }
    public decimal TotalReleasedAmount { get; set; }
    public decimal TotalWithdrawnAmount { get; set; }
}

public sealed class CircleFinancialStatsDto
{
    public decimal ContributionAmount { get; set; }
    public decimal RoundPool { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal TotalDepositsHeld { get; set; }
    public decimal TotalContributedAmount { get; set; }
}

public sealed class CircleRoundInfoDto
{
    public int CurrentRound { get; set; }
    public int TotalRounds { get; set; }
    public decimal RoundCollected { get; set; }
    public int RoundCollectedCount { get; set; }
    public DateTimeOffset? NextCollectionDate { get; set; }
    public DateTimeOffset? NextPayoutDate { get; set; }
    public CircleCurrentRecipientDto? CurrentRecipient { get; set; }
}

public sealed class CircleCurrentRecipientDto
{
    public long MemberId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int? RotationPosition { get; set; }
}

public sealed class CircleRulesDto
{
    public string Frequency { get; set; } = string.Empty;
    public string FrequencyDisplay { get; set; } = string.Empty;
    public decimal PenaltyAmount { get; set; }
    public string PenaltySource { get; set; } = string.Empty;
    public int GracePeriodHours { get; set; }
    public string RotationStrategy { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public string StartPolicy { get; set; } = string.Empty;
    public bool ModeratorStartsCircle { get; set; }
}

public sealed class CircleMemberStatsDto
{
    public int MemberCount { get; set; }
    public int ActiveMemberCount { get; set; }
    public int SlotsRemaining { get; set; }
    public int PaidThisRound { get; set; }
}

public sealed class CircleInviteInfoDto
{
    public string InviteCode { get; set; } = string.Empty;
    public bool InviteOnly { get; set; }
}