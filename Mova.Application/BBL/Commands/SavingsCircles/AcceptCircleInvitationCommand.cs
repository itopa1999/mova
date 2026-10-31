using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.SavingsCircles;

public sealed class AcceptCircleInvitationCommand
{
    public sealed class Command : IRequest<BaseResult<AcceptCircleInvitationResponse>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;

        [Required]
        public string InviteCode { get; set; } = string.Empty;

        public long CategoryId { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<AcceptCircleInvitationResponse>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<AcceptCircleInvitationResponse>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "AcceptCircleInvitation",
                ("UserId", request.UserPublicId),
                ("InviteCode", request.InviteCode));

            // ── 1. Basic input validation ────────────────────────────────
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("Missing UserPublicId.");
                return Bad("You must be signed in to accept an invitation.",
                    HttpStatusCode.Unauthorized);
            }

            if (string.IsNullOrWhiteSpace(request.InviteCode))
            {
                return Bad("Invite code is required.");
            }

            // ── 2. Resolve user identity (same pattern as CreateCircle) ──
            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId, cancellationToken);

            if (user is null)
            {
                op.Error("User not found.");
                return Bad("User account not found.", HttpStatusCode.NotFound);
            }

            var userEmail = user.Email;
            var userPhone = user.PhoneNumber;
            var userDisplayName = FirstNonEmpty(
                $"{user.FirstName} {user.LastName}".Trim(),
                userEmail);

            var category = await _unitOfWork.Query<WalletCategory>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.CategoryId && x.WalletType == WalletType.Circle, cancellationToken);

            if (category is null) return Bad("The selected category does not exist or is not available for circles.", HttpStatusCode.BadRequest);


            // ── 3. Load the circle by invite code ────────────────────────
            var normalizedCode = request.InviteCode.Trim().ToUpperInvariant();

            var circle = await _unitOfWork.Query<SavingsCircle>()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(
                    c => c.InviteCode != null &&
                         c.InviteCode.ToUpper() == normalizedCode,
                    cancellationToken);

            if (circle is null)
            {
                op.Warning("Circle not found for invite code.");
                return Bad("This invite link is invalid or the circle no longer exists.",
                    HttpStatusCode.NotFound);
            }

            // ── 4. Only Draft circles accept new members ─────────────────
            if (circle.Status != SavingsCircleStatus.Draft)
            {
                op.Warning($"Circle status is {circle.Status}; join rejected.");
                return Bad("This circle is no longer accepting new members.");
            }

            // ── 5. Look for an existing member row ───────────────────────
            var existingMember = circle.Members.FirstOrDefault(m =>
                (!string.IsNullOrWhiteSpace(m.UserPublicId) &&
                    m.UserPublicId == request.UserPublicId) ||
                (!string.IsNullOrWhiteSpace(m.Identifier) &&
                    !string.IsNullOrWhiteSpace(userEmail) &&
                    m.Identifier.Equals(userEmail, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(m.Phone) &&
                    !string.IsNullOrWhiteSpace(userPhone) &&
                    m.Phone.Equals(userPhone, StringComparison.OrdinalIgnoreCase)));

            // ── 6. Idempotent short-circuit for already-active users ─────
            if (existingMember?.Status == CircleMemberStatus.Active ||
                existingMember?.Status == CircleMemberStatus.Completed)
            {
                op.Success("User is already an active member.");
                return new BaseResult<AcceptCircleInvitationResponse>(
                    HttpStatusCode.OK,
                    "You are already a member of this circle.",
                    new AcceptCircleInvitationResponse { Notification = false });
            }

            // ── 7. Block re-entry for exited / defaulted members ─────────
            if (existingMember?.Status == CircleMemberStatus.Defaulted ||
                existingMember?.Status == CircleMemberStatus.Exited)
            {
                op.Warning(
                    $"User has terminal status {existingMember.Status}; join rejected.");
                return Bad("You cannot rejoin this circle.");
            }

            // ── 8. Invite-only enforcement ───────────────────────────────
            if (circle.InviteOnly && existingMember is null)
            {
                op.Warning(
                    $"User {request.UserPublicId} is not invited to a private circle.");
                return Bad("You are not invited to this circle.",
                    HttpStatusCode.Forbidden);
            }

            // ── 9. Capacity check ────────────────────────────────────────
            var activeMemberCount = circle.Members.Count(
                m => m.Status == CircleMemberStatus.Active);

            if (activeMemberCount >= circle.MemberCount)
            {
                op.Warning(
                    $"Circle is full ({activeMemberCount}/{circle.MemberCount}).");
                return Bad("This circle is full.");
            }

            // ── 10. Rotation position ────────────────────────────────────
            int? rotationPosition = null;
            if (circle.RotationStrategy != RotationStrategy.RandomDraw)
            {
                if (existingMember?.RotationPosition is not null)
                {
                    rotationPosition = existingMember.RotationPosition;
                }
                else
                {
                    var maxPosition = circle.Members
                        .Where(m => m.RotationPosition.HasValue)
                        .Select(m => m.RotationPosition!.Value)
                        .DefaultIfEmpty(0)
                        .Max();

                    rotationPosition = maxPosition + 1;
                }
            }

            // ── 11. Fees (same helper + pattern as CreateCircle) ─────────
            var contributionAmount = circle.ContributionAmount.ToDecimal();
            var depositAmount = circle.DepositAmount.ToDecimal();
            var needsWallet = existingMember?.WalletId is null or <= 0;

            var fees = WalletFeeHelper.Calculate(
                targetAmount: contributionAmount * circle.MemberCount,
                releaseAmount: contributionAmount * circle.MemberCount,
                payoutDestination: PayoutDestination.Circle.ToString(),
                releases: 1);

            var totalMovaFees = fees.TotalMovaCharges;
            var totalUpfrontCharge = contributionAmount + totalMovaFees + depositAmount;

            // ── 12. Balance check (same pattern as CreateCircle) ─────────
            if (needsWallet)
            {
                var userBalance = user.Balance.ToDecimal();
                if (userBalance < totalUpfrontCharge)
                {
                    op.Warning(
                        $"Insufficient balance for user {request.UserPublicId}. " +
                        $"Required: {totalUpfrontCharge}, Available: {userBalance}.");

                    return Bad(
                        $"You do not have enough balance to join this circle. " +
                        $"You need {totalUpfrontCharge:N0} but you only have {userBalance:N0}. " +
                        $"Please add funds and try again.");
                }
            }

            // ── 13. Upsert member row ────────────────────────────────────
            if (existingMember is not null)
            {
                existingMember.Status = CircleMemberStatus.Active;
                existingMember.AcceptedAt = DateTimeOffset.UtcNow;
                existingMember.UserPublicId = request.UserPublicId;
                existingMember.RotationPosition = rotationPosition;

                if (!string.IsNullOrWhiteSpace(userPhone))
                    existingMember.Phone = userPhone;

                if (string.IsNullOrWhiteSpace(existingMember.DisplayName) &&
                    !string.IsNullOrWhiteSpace(userDisplayName))
                    existingMember.DisplayName = userDisplayName;
            }
            else
            {
                existingMember = new CircleMember
                {
                    SavingsCircleId = circle.Id,
                    Identifier = userEmail ?? string.Empty,
                    UserPublicId = request.UserPublicId,
                    Phone = userPhone,
                    DisplayName = userDisplayName,
                    InvitedAt = DateTimeOffset.UtcNow,
                    AcceptedAt = DateTimeOffset.UtcNow,
                    RotationPosition = rotationPosition,
                    Status = CircleMemberStatus.Active
                };

                await _unitOfWork.AddAsync(existingMember, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ── 14. Queue wallet creation (only if missing) ──────────────
            if (needsWallet)
            {
                _notificationQueue.QueueCreateCircleWalletAsync(
                    request.UserPublicId,
                    category.Id,
                    circle.Id,
                    circle.Name,
                    circle.Description ?? string.Empty,
                    contributionAmount,
                    totalUpfrontCharge,
                    totalMovaFees,
                    depositAmount);

                op.Success("Circle wallet creation queued.");
            }
            else
            {
                op.Success(
                    $"Member already has wallet {existingMember.WalletId}; skipping queue.");
            }

            // ── 15. In-app notification (same pattern as CreateCircle) ───
            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                $"You've joined {circle.Name}",
                $"You're now member " +
                $"#{existingMember.RotationPosition?.ToString() ?? "TBD"} " +
                $"of {circle.MemberCount} in '{circle.Name}'. " +
                $"₦{contributionAmount:N0} / " +
                $"{circle.Frequency.ToString().ToLowerInvariant()}, " +
                $"round pool ₦{contributionAmount * circle.MemberCount:N0}. " +
                $"A dedicated circle wallet is being set up for you.",
                $"/dashboard/circles/details/{circle.Id}",
                null,
                CancellationToken.None);

            // ── 16. Return — notification flag only ──────────────────────
            op.Success(
                $"User joined circle {circle.Id} at position " +
                $"{rotationPosition?.ToString() ?? "pending-draw"}.");

            return new BaseResult<AcceptCircleInvitationResponse>(
                HttpStatusCode.OK,
                "Successfully joined the circle.",
                new AcceptCircleInvitationResponse { Notification = true });
        }

        private static BaseResult<AcceptCircleInvitationResponse> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    return v!;
            return string.Empty;
        }
    }
}

public sealed class AcceptCircleInvitationResponse
{
    public bool Notification { get; set; }
}