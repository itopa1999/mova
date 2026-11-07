using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.ExternalAPI;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common.Results;
using Mova.Shared.Logging;
using static Mova.Application.Interfaces.Notification.INotificationQueue;

namespace Mova.Application.BBL.Commands.SavingsCircles;

public sealed class InviteCircleMembersCommand
{
    public sealed class Command : IRequest<BaseResult<InviteMembersResponseDto>>
    {
        [JsonIgnore] 
        public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long CircleId { get; set; }

        [Required, MinLength(1)]
        public List<InviteMemberDto> Invites { get; set; } = new();
    }

    public sealed class InviteMemberDto
    {
        [Required, StringLength(320)]
        public string Identifier { get; set; } = string.Empty;

        [StringLength(100)]
        public string? TargetUserPublicId { get; set; }

        [StringLength(20)]
        public string? Phone { get; set; }

        [StringLength(120)]
        public string? DisplayName { get; set; }
    }

    public sealed class InviteMembersResponseDto
    {
        public int InvitedCount { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<InviteMembersResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<InviteMembersResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "InviteCircleMembers",
                ("UserId", request.UserPublicId),
                ("CircleId", request.CircleId));

            var circle = await _unitOfWork.Query<SavingsCircle>()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.Id == request.CircleId, cancellationToken);

            if (circle is null)
            {
                op.Warning("Circle not found.");
                return Bad("Circle not found.", HttpStatusCode.NotFound);
            }

            if (circle.OrganizerUserPublicId != request.UserPublicId)
            {
                op.Warning($"Unauthorized invite attempt by user {request.UserPublicId}. Organizer is {circle.OrganizerUserPublicId}.");
                return Bad("Only the circle organizer can invite members.", HttpStatusCode.Forbidden);
            }

            if (!circle.InviteOnly)
            {
                op.Warning($"Invitation rejected: Circle {circle.Id} is not set to InviteOnly.");
                return Bad("This circle is open to anyone with the link. You cannot send targeted invites.", HttpStatusCode.BadRequest);
            }

            if (circle.Status != SavingsCircleStatus.Draft)
            {
                op.Warning($"Cannot invite: Circle status is {circle.Status}. Invites only allowed in Draft.");
                return Bad("You can only invite members while the circle is in Draft status.", HttpStatusCode.BadRequest);
            }

            var currentMemberCount = circle.Members.Count(m => m.Status != CircleMemberStatus.Exited);
            if (currentMemberCount + request.Invites.Count > circle.MemberCount)
            {
                op.Warning($"Circle {circle.Id} would exceed capacity. Current: {currentMemberCount}, Requested: {request.Invites.Count}, Max: {circle.MemberCount}.");
                return Bad($"Adding {request.Invites.Count} members would exceed the circle limit of {circle.MemberCount}. You can only invite {circle.MemberCount - currentMemberCount} more.", HttpStatusCode.BadRequest);
            }

            var seenIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inviteDataList = new List<CircleInviteData>();

            foreach (var invite in request.Invites)
            {
                var normalizedIdentifier = (invite.Identifier ?? string.Empty).Trim().ToLowerInvariant();
                
                if (string.IsNullOrWhiteSpace(normalizedIdentifier) || !IsEmail(normalizedIdentifier))
                {
                    op.Warning($"Invalid email format: {invite.Identifier}");
                    return Bad($"Invalid invite email: '{invite.Identifier}'.");
                }

                if (!seenIdentifiers.Add(normalizedIdentifier))
                {
                    op.Warning($"Duplicate invite in payload: {normalizedIdentifier}");
                    return Bad($"Duplicate invite: '{normalizedIdentifier}' in the request.");
                }

                if (circle.Members.Any(m => string.Equals(m.Identifier, normalizedIdentifier, StringComparison.OrdinalIgnoreCase)))
                {
                    op.Warning($"Already a member/invited: {normalizedIdentifier}");
                    return Bad($"This person ({normalizedIdentifier}) has already been invited or is already a member.");
                }

                var isMovaUser = 
                    !string.IsNullOrWhiteSpace(invite.TargetUserPublicId) || 
                    !string.IsNullOrWhiteSpace(invite.DisplayName);

                if (isMovaUser && string.IsNullOrWhiteSpace(invite.TargetUserPublicId) && string.IsNullOrWhiteSpace(invite.DisplayName))
                {
                    op.Warning($"TargetUserPublicId or DisplayName required for MOVA invite: {normalizedIdentifier}");
                    return Bad($"Provide UserPublicId or DisplayName for MOVA invite '{normalizedIdentifier}'.");
                }

                var displayName = string.IsNullOrWhiteSpace(invite.DisplayName)
                    ? normalizedIdentifier.Split('@')[0]
                    : invite.DisplayName.Trim();

                inviteDataList.Add(new CircleInviteData
                {
                    Identifier = normalizedIdentifier,
                    TargetUserPublicId = isMovaUser ? invite.TargetUserPublicId?.Trim() : null,
                    Phone = isMovaUser ? invite.Phone?.Trim() : null,
                    DisplayName = displayName,
                    PayoutPosition = null 
                });
            }

            var organizer = circle.Members.FirstOrDefault(m => m.UserPublicId == circle.OrganizerUserPublicId);
            var organizerName = organizer?.DisplayName ?? "The organizer";
            var inviteCode = circle.InviteCode ?? string.Empty;

            foreach (var invite in inviteDataList)
            {
                if (!string.IsNullOrWhiteSpace(invite.TargetUserPublicId))
                {
                    _notificationQueue.InAppNotificationAsync(
                        invite.TargetUserPublicId,
                        NotificationType.Wallet,
                        $"You've been invited to {circle.Name}",
                        $"{organizerName} invited you to join '{circle.Name}' — " +
                        $"{circle.MemberCount} members, ₦{circle.ContributionAmount.ToDecimal():N0} / " +
                        $"{circle.Frequency.ToString().ToLowerInvariant()}. Tap to accept the invitation.",
                        $"/circle/wallet/consent/{inviteCode}",
                        null,
                        cancellationToken);
                }
            }

            _notificationQueue.QueueCreateCircleInvitationMemberAsync(
                circle.Id,
                organizerName,
                inviteCode,
                inviteDataList);

            op.Success($"Successfully queued {inviteDataList.Count} invites for circle {circle.Id}.");

            return new BaseResult<InviteMembersResponseDto>(
                HttpStatusCode.OK,
                $"{inviteDataList.Count} invitations queued for sending.",
                new InviteMembersResponseDto
                {
                    InvitedCount = inviteDataList.Count,
                    Message = $"{inviteDataList.Count} invitations queued for sending."
                });
        }

        private static bool IsEmail(string identifier)
            => Regex.IsMatch(identifier, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");

        private static BaseResult<InviteMembersResponseDto> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);
    }
}