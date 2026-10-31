using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Queries.SavingsCircles;

public sealed class GetCircleMembersQuery
{
    public sealed class Query : IRequest<BaseResult<CircleMembersResponseDto>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;

        [Required]
        public long WalletId { get; set; }

        public string? StatusFilter { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<CircleMembersResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult<CircleMembersResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "GetCircleMembers",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId),
                ("StatusFilter", request.StatusFilter ?? "all"));

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.Id == request.WalletId &&
                         w.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Warning("Wallet not found or not owned by user.");
                return new BaseResult<CircleMembersResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Type != WalletType.Circle || wallet.SavingsCircleId is null)
            {
                op.Warning("Wallet is not a circle wallet.");
                return new BaseResult<CircleMembersResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet is not linked to a savings circle.");
            }

            var circle = await _unitOfWork.Query<SavingsCircle>()
                .AsNoTracking()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(
                    c => c.Id == wallet.SavingsCircleId.Value,
                    cancellationToken);

            if (circle is null)
            {
                op.Warning("Circle not found.");
                return new BaseResult<CircleMembersResponseDto>(
                    HttpStatusCode.NotFound,
                    "The circle linked to this wallet no longer exists.");
            }

            var currentMember = circle.Members.FirstOrDefault(m =>
                !string.IsNullOrWhiteSpace(m.UserPublicId) &&
                m.UserPublicId == request.UserPublicId);

            if (currentMember is null)
            {
                op.Warning("User is not a member of this circle.");
                return new BaseResult<CircleMembersResponseDto>(
                    HttpStatusCode.Forbidden,
                    "You are not a member of this circle.");
            }

            var isCallerOrganizer =
                currentMember.UserPublicId == circle.OrganizerUserPublicId;

            var currentRoundEntity = await _unitOfWork.Query<CircleRound>()
                .AsNoTracking()
                .Where(r =>
                    r.SavingsCircleId == circle.Id &&
                    (r.Status == CircleRoundStatus.Pending ||
                     r.Status == CircleRoundStatus.Collecting))
                .OrderByDescending(r => r.RoundNumber)
                .FirstOrDefaultAsync(cancellationToken);

            var currentRoundNumber = currentRoundEntity?.RoundNumber ?? 0;

            var contributionStatusByMember = new Dictionary<long, ContributionStatus>();

            if (currentRoundEntity is not null)
            {
                var contributions = await _unitOfWork.Query<Contribution>()
                    .AsNoTracking()
                    .Where(c => c.CircleRoundId == currentRoundEntity.Id)
                    .Select(c => new { c.CircleMemberId, c.Status })
                    .ToListAsync(cancellationToken);

                contributionStatusByMember = contributions
                    .ToDictionary(c => c.CircleMemberId, c => c.Status);
            }

            var requestedStatus = (request.StatusFilter ?? "all").Trim().ToLowerInvariant();

            IEnumerable<CircleMember> filteredMembers = requestedStatus switch
            {
                "active" => circle.Members.Where(m => m.Status == CircleMemberStatus.Active),
                "invited" => circle.Members.Where(m => m.Status == CircleMemberStatus.Invited),
                "exited" => circle.Members.Where(m => m.Status == CircleMemberStatus.Exited),
                "defaulted" => circle.Members.Where(m => m.Status == CircleMemberStatus.Defaulted),
                "completed" => circle.Members.Where(m => m.Status == CircleMemberStatus.Completed),
                _ => circle.Members
            };

            var memberDtos = filteredMembers
                .OrderBy(m => m.RotationPosition ?? 999)
                .ThenBy(m => m.InvitedAt)
                .Select(m =>
                {
                    var memberStatus = m.Status;

                    var isRecipientThisRound =
                        currentRoundNumber > 0 &&
                        m.RotationPosition == currentRoundNumber &&
                        memberStatus == CircleMemberStatus.Active;

                    string contributionStatusThisRound;
                    if (memberStatus == CircleMemberStatus.Invited ||
                        memberStatus == CircleMemberStatus.Exited)
                    {
                        contributionStatusThisRound = "NotStarted";
                    }
                    else if (memberStatus == CircleMemberStatus.Defaulted)
                    {
                        contributionStatusThisRound = "Defaulted";
                    }
                    else if (contributionStatusByMember.TryGetValue(m.Id, out var contribStatus))
                    {
                        contributionStatusThisRound = contribStatus.ToString();
                    }
                    else if (currentRoundNumber == 0)
                    {
                        contributionStatusThisRound = "NotStarted";
                    }
                    else
                    {
                        contributionStatusThisRound = "Pending";
                    }

                    return new CircleMemberItemDto
                    {
                        UserPublicId = m.UserPublicId ?? string.Empty,
                        DisplayName = m.DisplayName ?? "Member",
                        Phone = ResolvePhone(m.Phone, isCallerOrganizer),
                        Status = memberStatus.ToString(),
                        IsOrganizer = m.UserPublicId == circle.OrganizerUserPublicId,
                        IsYou = m.UserPublicId == request.UserPublicId,
                        RotationPosition = m.RotationPosition,
                        JoinedAt = m.InvitedAt,
                        AcceptedAt = m.AcceptedAt,
                        TotalContributed = m.TotalContributed.ToDecimal(),
                        TotalReceived = m.TotalReceived.ToDecimal(),
                        DepositHeld = m.DepositHeld.ToDecimal(),
                        MissedContributions = m.MissedContributions,
                        HasReceivedPayout = m.PayoutReceivedAt is not null,
                        PayoutReceivedAt = m.PayoutReceivedAt,
                        IsRecipientThisRound = isRecipientThisRound,
                        ContributionStatusThisRound = contributionStatusThisRound
                    };
                })
                .ToList();

            var dto = new CircleMembersResponseDto
            {
                Members = memberDtos
            };

            op.Success(
                $"Members retrieved. CircleId: {circle.Id}, " +
                $"Returned: {memberDtos.Count}, Filter: {requestedStatus}, " +
                $"OrganizerView: {isCallerOrganizer}.");

            return new BaseResult<CircleMembersResponseDto>(
                HttpStatusCode.OK,
                "Circle members retrieved successfully.",
                dto);
        }

        private static string? ResolvePhone(string? rawPhone, bool isCallerOrganizer)
        {
            if (string.IsNullOrWhiteSpace(rawPhone))
                return null;

            var phone = rawPhone.Trim();

            return isCallerOrganizer ? phone : MaskPhone(phone);
        }

        private static string MaskPhone(string phone)
        {
            if (phone.Length <= 4)
                return new string('*', phone.Length);

            if (phone.Length <= 8)
            {
                var visible = 2;
                return phone.Substring(0, visible)
                    + new string('*', phone.Length - visible * 2)
                    + phone.Substring(phone.Length - visible);
            }

            return phone.Substring(0, 4)
                + new string('*', phone.Length - 8)
                + phone.Substring(phone.Length - 4);
        }
    }
}

public sealed class CircleMembersResponseDto
{
    public List<CircleMemberItemDto> Members { get; set; } = new();
}

public sealed class CircleMemberItemDto
{
    public string UserPublicId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOrganizer { get; set; }
    public bool IsYou { get; set; }
    public int? RotationPosition { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public decimal TotalContributed { get; set; }
    public decimal TotalReceived { get; set; }
    public decimal DepositHeld { get; set; }
    public int MissedContributions { get; set; }
    public bool HasReceivedPayout { get; set; }
    public DateTimeOffset? PayoutReceivedAt { get; set; }
    public bool IsRecipientThisRound { get; set; }
    public string ContributionStatusThisRound { get; set; } = string.Empty;
}