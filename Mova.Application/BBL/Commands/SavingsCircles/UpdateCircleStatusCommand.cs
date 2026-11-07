using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Commands.SavingsCircles;

public sealed class UpdateCircleStatusCommand
{
    public sealed class Command : IRequest<BaseResult<UpdateCircleStatusResponse>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;
        public long WalletId { get; set; }

        [Required]
        public string TargetStatus { get; set; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<UpdateCircleStatusResponse>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ISavingsCircleService _savingsCircleService;
        private readonly ICacheService _cacheService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ISavingsCircleService savingsCircleService,
            ICacheService cacheService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _savingsCircleService = savingsCircleService;
            _cacheService = cacheService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<UpdateCircleStatusResponse>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "UpdateCircleStatus",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId),
                ("TargetStatus", request.TargetStatus));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
                return Bad("You must be signed in.", HttpStatusCode.Unauthorized);

            var wallet = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == request.WalletId && w.Type == WalletType.Circle, cancellationToken);

            if (wallet is null || wallet.SavingsCircleId is null)
                return Bad("Circle wallet not found.", HttpStatusCode.NotFound);

            var circle = await _unitOfWork.Query<SavingsCircle>()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.Id == wallet.SavingsCircleId, cancellationToken);

            if (circle is null)
                return Bad("Circle not found.", HttpStatusCode.NotFound);

            if (circle.OrganizerUserPublicId != request.UserPublicId)
                return Bad("Only the organizer can change the circle status.", HttpStatusCode.Forbidden);

            if (!Enum.TryParse<SavingsCircleStatus>(request.TargetStatus, true, out var targetStatus))
                return Bad("Invalid target status.");

            var currentStatus = circle.Status;

            if (targetStatus == SavingsCircleStatus.Active)
            {
                if (currentStatus == SavingsCircleStatus.Draft)
                {
                    var activeMembers = circle.Members
                        .Where(m => m.Status == CircleMemberStatus.Active)
                        .ToList();

                    if (activeMembers.Count < 3)
                        return Bad($"You need at least 3 active members to start. Currently: {activeMembers.Count}.");

                    if (circle.ContributionAmount.ToDecimal() <= 0)
                        return Bad("Contribution amount must be greater than zero to start the circle.");

                    var contributionAmount = circle.ContributionAmount.ToDecimal();
                    var depositPerMember = circle.DepositAmount.ToDecimal();

                    if (depositPerMember > 0 && depositPerMember < contributionAmount)
                    {
                        op.Warning($"Circle deposit ({depositPerMember:N0}) is less than the contribution ({contributionAmount:N0}).");
                        return Bad($"The security deposit per member ({depositPerMember:N0}) must be at least the contribution amount ({contributionAmount:N0}) to start the circle. Please update the circle rules first.");
                    }

                    var shortMembers = activeMembers
                        .Where(m => m.DepositHeld.ToDecimal() < contributionAmount)
                        .ToList();

                    if (shortMembers.Count > 0)
                    {
                        op.Warning($"{shortMembers.Count} member(s) have insufficient deposit to cover the first contribution of {contributionAmount}.");
                        return Bad($"{shortMembers.Count} active member(s) don't have enough deposit to cover the first round contribution ({contributionAmount:N0}). Ask them to top up their deposit before starting the circle.");
                    }

                    if (circle.RotationStrategy != RotationStrategy.RandomDraw)
                    {
                        var membersWithoutPosition = activeMembers.Where(m => !m.RotationPosition.HasValue).ToList();
                        if (membersWithoutPosition.Count > 0)
                        {
                            op.Warning($"{membersWithoutPosition.Count} active member(s) do not have a rotation position assigned.");
                            return Bad($"All active members must have a payout position before the circle can start. {membersWithoutPosition.Count} member(s) are missing a position. Please assign positions first.");
                        }
                    }

                    if (circle.RotationStrategy == RotationStrategy.RandomDraw)
                    {
                        var random = Random.Shared;
                        for (var i = activeMembers.Count - 1; i > 0; i--)
                        {
                            var j = random.Next(i + 1);
                            (activeMembers[i], activeMembers[j]) = (activeMembers[j], activeMembers[i]);
                        }
                        for (var i = 0; i < activeMembers.Count; i++)
                        {
                            activeMembers[i].RotationPosition = i + 1;
                        }
                    }

                    var orderedMembers = activeMembers
                        .OrderBy(m => m.RotationPosition ?? int.MaxValue)
                        .ThenBy(m => m.AcceptedAt ?? m.InvitedAt)
                        .ToList();

                    var startedAt = DateTimeOffset.UtcNow;

                    circle.MemberCount = orderedMembers.Count;
                    circle.Status = SavingsCircleStatus.Active;
                    circle.StartDate = startedAt;

                    var firstCollectionDate = _savingsCircleService.GetNextCollectionDate(
                        circle.Frequency,
                        circle.FrequencyConfig,
                        DateTimeOffset.UtcNow);

                    circle.NextCollectionDate = firstCollectionDate;
                    circle.NextPayoutDate = firstCollectionDate;

                    var roundOneRecipient = orderedMembers[0];
                    var firstRound = new CircleRound
                    {
                        SavingsCircleId = circle.Id,
                        RoundNumber = 1,
                        CollectionDate = firstCollectionDate,
                        PayoutDate = firstCollectionDate,
                        RecipientUserPublicId = roundOneRecipient.UserPublicId,
                        PooledAmount = Money.FromNaira(0),
                        PaidOutAmount = Money.FromNaira(0),
                        Status = CircleRoundStatus.Collecting,
                        RequiresManualReview = false
                    };
                    
                    await _unitOfWork.AddAsync(firstRound, cancellationToken);
                    _unitOfWork.Update(circle);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    await InvalidateCircleCacheAsync(circle.Id, cancellationToken);
                    await NotifyMembersAsync(circle, $"The circle '{circle.Name}' has been started by the organizer. Round 1 is now collecting.", cancellationToken);

                    op.Success("Circle started successfully.");
                    return Ok("Circle started successfully. Round 1 is now collecting.");
                }
                else if (currentStatus == SavingsCircleStatus.Suspended)
                {
                    circle.Status = SavingsCircleStatus.Active;
                    _unitOfWork.Update(circle);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    await InvalidateCircleCacheAsync(circle.Id, cancellationToken);
                    await NotifyMembersAsync(circle, $"The circle '{circle.Name}' has been resumed by the organizer.", cancellationToken);

                    op.Success("Circle resumed successfully.");
                    return Ok("Circle resumed successfully.");
                }
                else
                {
                    return Bad($"Cannot transition from {currentStatus} to Active.");
                }
            }
            else if (targetStatus == SavingsCircleStatus.Suspended)
            {
                if (currentStatus == SavingsCircleStatus.Active)
                {
                    circle.Status = SavingsCircleStatus.Suspended;
                    _unitOfWork.Update(circle);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    await InvalidateCircleCacheAsync(circle.Id, cancellationToken);
                    await NotifyMembersAsync(circle, $"The circle '{circle.Name}' has been suspended by the organizer.", cancellationToken);

                    op.Success("Circle suspended successfully.");
                    return Ok("Circle suspended successfully.");
                }
                else
                {
                    return Bad($"Cannot transition from {currentStatus} to Suspended.");
                }
            }

            return Bad("Invalid status transition requested.");
        }

        private async Task InvalidateCircleCacheAsync(long circleId, CancellationToken cancellationToken)
        {
            await _cacheService.DeletePrefixAsync(CacheKeys.CircleMembersPrefix(circleId), cancellationToken);
            await _cacheService.DeletePrefixAsync(CacheKeys.CircleRoundsPrefix(circleId), cancellationToken);
            await _cacheService.DeletePrefixAsync(CacheKeys.CircleContributionsPrefix(circleId), cancellationToken);
        }

        private async Task NotifyMembersAsync(SavingsCircle circle, string message, CancellationToken cancellationToken)
        {
            var activeMembers = circle.Members
                .Where(m => m.Status == CircleMemberStatus.Active && m.UserPublicId != circle.OrganizerUserPublicId)
                .ToList();

            foreach (var member in activeMembers)
            {
                if (!string.IsNullOrWhiteSpace(member.UserPublicId))
                {
                    _notificationQueue.InAppNotificationAsync(
                        member.UserPublicId,
                        NotificationType.Wallet,
                        $"Circle {circle.Status}",
                        message,
                        $"/dashboard/circles/details/{circle.Id}",
                        null,
                        cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(member.Identifier))
                {
                    _notificationQueue.QueueNotificationEmail(
                        member.DisplayName ?? "Member",
                        member.Identifier,
                        $"# Update from {circle.Name}\n\n" +
                        $"**{message}**\n\n" +
                        $"- You can view the latest details in the app.",
                        $"Update from {circle.Name}");
                }
            }
        }

        private static BaseResult<UpdateCircleStatusResponse> Ok(string message)
            => new(HttpStatusCode.OK, message, new UpdateCircleStatusResponse { Updated = true, Notification = true });

        private static BaseResult<UpdateCircleStatusResponse> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);
    }
}

public sealed class UpdateCircleStatusResponse
{
    public bool Updated { get; set; }
    public bool Notification { get; set; }
}