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

public sealed class GetCircleConsentQuery
{
    public sealed class Query : IRequest<BaseResult<CircleConsentDto>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;
        [JsonIgnore] public string UserEmail { get; set; } = string.Empty;

        [Required]
        public string InviteCode { get; set; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<CircleConsentDto>>
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

        public async Task<BaseResult<CircleConsentDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "GetCircleConsent",
                ("UserId", request.UserPublicId),
                ("InviteCode", request.InviteCode));

            var normalizedInviteCode = request.InviteCode.Trim().ToUpperInvariant();
            var circle = await _unitOfWork.Query<SavingsCircle>()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(
                    c => c.InviteCode != null && c.InviteCode.ToUpper() == normalizedInviteCode,
                    cancellationToken);

            if (circle is null)
            {
                op.Warning("Circle not found for provided invite code.");
                return new BaseResult<CircleConsentDto>(
                    HttpStatusCode.NotFound,
                    "This invite link is invalid or the circle no longer exists.");
            }

            var currentMember = circle.Members.FirstOrDefault(m =>
                (!string.IsNullOrWhiteSpace(m.UserPublicId) && m.UserPublicId == request.UserPublicId) ||
                (!string.IsNullOrWhiteSpace(m.Identifier) && m.Identifier.Equals(request.UserEmail, StringComparison.OrdinalIgnoreCase)));

            if (circle.InviteOnly && currentMember is null)
            {
                op.Warning($"Unauthorized access attempt. User {request.UserPublicId} is not invited to a private circle.");
                return new BaseResult<CircleConsentDto>(
                    HttpStatusCode.Forbidden,
                    "You are not invited to this circle.");
            }

            bool alreadyJoined = currentMember?.Status == CircleMemberStatus.Active ||
                                 currentMember?.Status == CircleMemberStatus.Completed;

            bool hasPendingInvite = currentMember?.Status == CircleMemberStatus.Invited;

            bool canJoin = hasPendingInvite || (currentMember is null && !circle.InviteOnly);

            var activeMemberCount = circle.Members.Count(m => m.Status == CircleMemberStatus.Active);
            var slotsRemaining = Math.Max(0, circle.MemberCount - activeMemberCount);

            if (alreadyJoined) op.Success("User is already an active member.");
            else if (hasPendingInvite) op.Success("User has a pending invite.");
            else op.Success("User can join via public link.");

            var organizer = await _identityService.GetByIdentifierAsync(circle.OrganizerUserPublicId, cancellationToken);
            var organizerName = organizer is not null
                ? $"{organizer.FirstName} {organizer.LastName}".Trim()
                : "Organizer";

            if (string.IsNullOrWhiteSpace(organizerName))
                organizerName = "Organizer";

            var categoryId = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .Where(w =>
                    w.SavingsCircleId == circle.Id &&
                    w.UserPublicId == circle.OrganizerUserPublicId &&
                    w.Type == WalletType.Circle)
                .Select(w => (long?)w.CategoryId)
                .FirstOrDefaultAsync(cancellationToken);

            var frequencyDisplay = _savingsCircleService.HumanizeFrequency(
                circle.Frequency,
                circle.FrequencyConfig ?? string.Empty);

            var dto = new CircleConsentDto
            {
                AccessState = new AccessStateDto
                {
                    AlreadyJoined = alreadyJoined,
                    HasPendingInvite = hasPendingInvite,
                    CanJoin = canJoin
                },
                CircleDetails = new CircleDetailsDto
                {
                    CircleId = circle.Id,
                    Name = circle.Name,
                    Description = circle.Description ?? string.Empty,
                    OrganizerName = organizerName,
                    Status = circle.Status.ToString(),
                    CategoryId = categoryId
                },
                FinancialStats = new FinancialStatsDto
                {
                    ContributionAmount = circle.ContributionAmount.ToDecimal(),
                    RoundPool = circle.ContributionAmount.ToDecimal() * circle.MemberCount,
                    DepositAmount = circle.DepositAmount.ToDecimal(),
                    TotalDepositsHeld = circle.Members.Sum(m => m.DepositHeld.ToDecimal()),
                    TotalContributedAmount = circle.Members.Sum(m => m.TotalContributed.ToDecimal()),
                    MyDepositHeld = currentMember?.DepositHeld.ToDecimal() ?? 0m
                },
                RulesAndSchedule = new RulesAndScheduleDto
                {
                    Frequency = circle.Frequency.ToString(),
                    FrequencyDisplay = frequencyDisplay,
                    PenaltyAmount = circle.PenaltyAmount.ToDecimal(),
                    PenaltySource = circle.PenaltyAmountFrom.ToString(),
                    GracePeriodHours = circle.GracePeriodHours,
                    RotationStrategy = circle.RotationStrategy.ToString(),
                    StartDate = circle.StartDate,
                    StartPolicy = circle.StartPolicy.ToString()
                },
                MemberStats = new MemberStatsDto
                {
                    MemberCount = circle.MemberCount,
                    ActiveMemberCount = activeMemberCount,
                    SlotsRemaining = slotsRemaining
                },
                Members = circle.Members
                    .Where(m => m.Status == CircleMemberStatus.Active || m.Status == CircleMemberStatus.Invited)
                    .OrderBy(m => m.RotationPosition ?? 999)
                    .Select(m => new ConsentMemberDto
                    {
                        DisplayName = m.DisplayName ?? "Member",
                        Status = m.Status.ToString(),
                        IsOrganizer = m.UserPublicId == circle.OrganizerUserPublicId,
                        RotationPosition = m.RotationPosition
                    })
                    .ToList()
            };

            return new BaseResult<CircleConsentDto>(
                HttpStatusCode.OK,
                "Circle details retrieved successfully.",
                dto);
        }
    }
}

public sealed class CircleConsentDto
{
    public AccessStateDto AccessState { get; set; } = new();
    public CircleDetailsDto CircleDetails { get; set; } = new();
    public FinancialStatsDto FinancialStats { get; set; } = new();
    public RulesAndScheduleDto RulesAndSchedule { get; set; } = new();
    public MemberStatsDto MemberStats { get; set; } = new();
    public List<ConsentMemberDto> Members { get; set; } = new();
}

public sealed class AccessStateDto
{
    public bool AlreadyJoined { get; set; }
    public bool HasPendingInvite { get; set; }
    public bool CanJoin { get; set; }
}

public sealed class CircleDetailsDto
{
    public long CircleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OrganizerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long? CategoryId { get; set; }
}

public sealed class FinancialStatsDto
{
    public decimal ContributionAmount { get; set; }
    public decimal RoundPool { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal TotalDepositsHeld { get; set; }
    public decimal TotalContributedAmount { get; set; }
    public decimal MyDepositHeld { get; set; }
}

public sealed class RulesAndScheduleDto
{
    public string Frequency { get; set; } = string.Empty;
    public string FrequencyDisplay { get; set; } = string.Empty;
    public decimal PenaltyAmount { get; set; }
    public string PenaltySource { get; set; } = string.Empty;
    public int GracePeriodHours { get; set; }
    public string RotationStrategy { get; set; } = string.Empty;
    public DateTimeOffset StartDate { get; set; }
    public string StartPolicy { get; set; } = string.Empty;
}

public sealed class MemberStatsDto
{
    public int MemberCount { get; set; }
    public int ActiveMemberCount { get; set; }
    public int SlotsRemaining { get; set; }
}

public sealed class ConsentMemberDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsOrganizer { get; set; }
    public int? RotationPosition { get; set; }
}