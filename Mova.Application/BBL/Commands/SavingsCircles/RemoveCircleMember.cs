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
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.SavingsCircles;

public sealed class RemoveCircleMemberCommand
{
    public sealed class Command : IRequest<BaseResult<RemoveCircleMemberResponse>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;

        public long WalletId { get; set; }

        [Required]
        public string TargetUserPublicId { get; set; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<RemoveCircleMemberResponse>>
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

        public async Task<BaseResult<RemoveCircleMemberResponse>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "RemoveCircleMember",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId),
                ("Target", request.TargetUserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("Missing UserPublicId.");
                return Bad("You must be signed in.", HttpStatusCode.Unauthorized);
            }

            if (string.IsNullOrWhiteSpace(request.TargetUserPublicId))
                return Bad("Target member is required.");

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.Id == request.WalletId && w.Type == WalletType.Circle,
                    cancellationToken);

            if (wallet is null || wallet.SavingsCircleId is null)
                return Bad("Circle wallet not found.", HttpStatusCode.NotFound);

            var circle = await _unitOfWork.Query<SavingsCircle>()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(
                    c => c.Id == wallet.SavingsCircleId,
                    cancellationToken);

            if (circle is null)
                return Bad("Circle not found.", HttpStatusCode.NotFound);

            if (circle.OrganizerUserPublicId != request.UserPublicId)
                return Bad("Only the organizer can remove members.",
                    HttpStatusCode.Forbidden);

            if (circle.Status == SavingsCircleStatus.Completed ||
                circle.Status == SavingsCircleStatus.Cancelled)
            {
                return Bad("Cannot remove members from a completed or cancelled circle.");
            }

            if (request.UserPublicId == request.TargetUserPublicId)
                return Bad("You cannot remove yourself from the circle.");

            var member = circle.Members
                .FirstOrDefault(m => m.UserPublicId == request.TargetUserPublicId);

            if (member is null)
                return Bad("That member is not part of this circle.",
                    HttpStatusCode.NotFound);

            if (member.Status == CircleMemberStatus.Exited ||
                member.Status == CircleMemberStatus.Completed)
            {
                return Bad("This member has already left the circle.");
            }

            if (circle.Status == SavingsCircleStatus.Active)
            {
                var currentRound = await _unitOfWork.Query<CircleRound>()
                    .AsNoTracking()
                    .Where(r =>
                        r.SavingsCircleId == circle.Id &&
                        r.Status == CircleRoundStatus.Collecting)
                    .OrderBy(r => r.RoundNumber)
                    .FirstOrDefaultAsync(cancellationToken);

                if (currentRound is not null &&
                    currentRound.RecipientUserPublicId == request.TargetUserPublicId)
                {
                    return Bad("Cannot remove a member who is the current round's recipient.");
                }

                if (member.TotalReceived.ToDecimal() > 0)
                    return Bad("Cannot remove a member who has already received a payout.");
            }

            var refundAmount = member.DepositHeld.ToDecimal();

            await using var tx = await _unitOfWork.BeginAtomicSerializableAsync(
                cancellationToken);

            if (refundAmount > 0)
            {
                var credited = await _identityService.CreditBalanceAsync(
                    request.TargetUserPublicId,
                    refundAmount,
                    cancellationToken);

                if (!credited)
                {
                    op.Error(
                        $"Failed to credit refund of {refundAmount} to " +
                        $"{request.TargetUserPublicId}.");
                    return Bad("Failed to refund the member's deposit. Please try again.");
                }
            }

            member.Status = CircleMemberStatus.Exited;
            member.DepositHeld = Money.FromNaira(0);
            member.RotationPosition = null;

            if (member.WalletId.HasValue)
            {
                var memberWallet = await _unitOfWork.Query<Wallet>()
                    .FirstOrDefaultAsync(
                        w => w.Id == member.WalletId.Value,
                        cancellationToken);

                if (memberWallet is not null)
                {
                    memberWallet.Status = WalletStatus.Closed;
                    memberWallet.LockedAmount = Money.FromNaira(0);
                    memberWallet.AvailableAmount = Money.FromNaira(0);
                }
            }

            var pendingContributions = await _unitOfWork.Query<Contribution>()
                .Where(c =>
                    c.CircleMemberId == member.Id &&
                    c.Status == ContributionStatus.Pending)
                .ToListAsync(cancellationToken);

            foreach (var contribution in pendingContributions)
            {
                contribution.Status = ContributionStatus.Failed;
                contribution.FailureReason = "Member removed from circle";
            }

            if (refundAmount > 0)
            {
                var refund = new Transaction
                {
                    UserPublicId = request.TargetUserPublicId,
                    WalletId = null,
                    Title = $"Refund: Removed from circle ({circle.Name})",
                    Provider = null,
                    Amount = Money.FromNaira(refundAmount),
                    Type = TransactionType.Refund,
                    Status = TransactionStatus.Completed,
                    Reference =
                        $"circle-member-removal-refund-{circle.Id}-" +
                        $"{request.TargetUserPublicId}-{Guid.NewGuid():N}",
                    CompletedAt = DateTimeOffset.UtcNow
                };

                await _unitOfWork.AddAsync(refund, cancellationToken);

                await _unitOfWork.AddAsync(new LedgerEntry
                {
                    WalletId = null,
                    TransactionId = refund.Id,
                    Amount = Money.FromNaira(refundAmount),
                    IsCredit = true
                }, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);

            _notificationQueue.InAppNotificationAsync(
                request.TargetUserPublicId,
                NotificationType.Wallet,
                $"You've been removed from {circle.Name}",
                refundAmount > 0
                    ? $"The organizer removed you from '{circle.Name}'. " +
                      $"Your deposit of ₦{refundAmount:N0} has been refunded to your balance."
                    : $"The organizer removed you from '{circle.Name}'.",
                "/dashboard/wallets",
                null,
                CancellationToken.None);

            op.Success(
                $"Member {member.Id} ({request.TargetUserPublicId}) removed " +
                $"from circle {circle.Id} by organizer {request.UserPublicId}. " +
                $"Refunded {refundAmount}.");

            return new BaseResult<RemoveCircleMemberResponse>(
                HttpStatusCode.OK,
                refundAmount > 0
                    ? $"Member removed. ₦{refundAmount:N0} refunded to their balance."
                    : "Member removed.",
                new RemoveCircleMemberResponse
                {
                    Removed = true,
                    RefundedAmount = refundAmount
                });
        }

        private static BaseResult<RemoveCircleMemberResponse> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);
    }
}

public sealed class RemoveCircleMemberResponse
{
    public bool Removed { get; set; }
    public decimal RefundedAmount { get; set; }
}