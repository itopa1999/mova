using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

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
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            ISavingsCircleService savingsCircleService,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _savingsCircleService = savingsCircleService;
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

                    if (depositPerMember < contributionAmount)
                    {
                        op.Warning(
                            $"Circle deposit ({depositPerMember:N0}) is less than the " +
                            $"contribution ({contributionAmount:N0}).");

                        return Bad(
                            $"The security deposit per member ({depositPerMember:N0}) must be " +
                            $"at least the contribution amount ({contributionAmount:N0}) to " +
                            $"start the circle. Please update the circle rules first.");
                    }

                    var shortMembers = activeMembers
                        .Where(m => m.DepositHeld.ToDecimal() < contributionAmount)
                        .ToList();

                    if (shortMembers.Count > 0)
                    {
                        op.Warning(
                            $"{shortMembers.Count} member(s) have insufficient deposit " +
                            $"to cover the first contribution of {contributionAmount}.");

                        return Bad(
                            $"{shortMembers.Count} active member(s) don't have enough deposit " +
                            $"to cover the first round contribution ({contributionAmount:N0}). " +
                            $"Ask them to top up their deposit before starting the circle.");
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

                    circle.MemberCount = orderedMembers.Count;
                    circle.Status = SavingsCircleStatus.Active;

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

                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    var totalPooled = 0m;
                    var now = DateTimeOffset.UtcNow;

                    foreach (var member in orderedMembers)
                    {
                        await _unitOfWork.AddAsync(new Contribution
                        {
                            CircleRoundId = firstRound.Id,
                            CircleMemberId = member.Id,
                            Amount = circle.ContributionAmount,
                            PenaltyApplied = Money.FromNaira(0),
                            Status = ContributionStatus.Collected,
                            CollectedAt = now,
                            PaymentReference = $"circle-start-{circle.Id}-{member.UserPublicId}",
                            FailureReason = null
                        }, cancellationToken);

                        var previousDeposit = member.DepositHeld.ToDecimal();
                        member.DepositHeld = Money.FromNaira(Math.Max(0, previousDeposit - contributionAmount));
                        member.TotalContributed = Money.FromNaira(member.TotalContributed.ToDecimal() + contributionAmount);

                        totalPooled += contributionAmount;

                        _unitOfWork.Update(member);
                    }

                    firstRound.PooledAmount = Money.FromNaira(totalPooled);
                    _unitOfWork.Update(firstRound);

                    _unitOfWork.Update(circle);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    op.Success("Circle started successfully.");
                    return Ok("Circle started successfully. Round 1 is now collecting.");
                }
                else if (currentStatus == SavingsCircleStatus.Suspended)
                {
                    circle.Status = SavingsCircleStatus.Active;
                    _unitOfWork.Update(circle);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

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

        private static BaseResult<UpdateCircleStatusResponse> Ok(string message)
            => new(HttpStatusCode.OK, message, new UpdateCircleStatusResponse { Updated = true });

        private static BaseResult<UpdateCircleStatusResponse> Bad(
            string message,
            HttpStatusCode status = HttpStatusCode.BadRequest)
            => new(status, message);
    }
}

public sealed class UpdateCircleStatusResponse
{
    public bool Updated { get; set; }
}