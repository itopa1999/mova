using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;

namespace Mova.Application.BBL.Commands.SavingsCircles;

public sealed class PayContributionCommand
{
    public sealed class Command : IRequest<BaseResult<bool>>
    {
        [JsonIgnore] public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long CircleId { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<bool>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _logger = logger;
        }

        public async Task<BaseResult<bool>> Handle(Command request, CancellationToken cancellationToken)
        {
            // Initialize OperationLogger
            using var op = OperationLogger.Start(
                _logger,
                "PayContribution",
                ("UserId", request.UserPublicId),
                ("CircleId", request.CircleId));

            var circle = await _unitOfWork.Query<SavingsCircle>()
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.Id == request.CircleId, cancellationToken);

            if (circle == null)
            {
                op.Warning($"Circle {request.CircleId} not found.");
                return new BaseResult<bool>(HttpStatusCode.NotFound, "Circle not found.");
            }

            var member = circle.Members.FirstOrDefault(m => m.UserPublicId == request.UserPublicId);
            if (member == null)
            {
                op.Warning($"User {request.UserPublicId} is not a member of circle {request.CircleId}.");
                return new BaseResult<bool>(HttpStatusCode.NotFound, "You are not a member of this circle.");
            }

            var currentRound = await _unitOfWork.Query<CircleRound>()
                .Include(r => r.Contributions)
                .FirstOrDefaultAsync(r => r.SavingsCircleId == circle.Id && r.Status == CircleRoundStatus.Collecting, cancellationToken);

            if (currentRound == null)
            {
                op.Warning($"No active collecting round found for circle {request.CircleId}.");
                return new BaseResult<bool>(HttpStatusCode.BadRequest, "No active round to pay for.");
            }

            var contrib = currentRound.Contributions.FirstOrDefault(c => c.CircleMemberId == member.Id);
            if (contrib != null && contrib.Status == ContributionStatus.Collected)
            {
                op.Warning($"User {request.UserPublicId} has already paid for Round {currentRound.RoundNumber} in circle {request.CircleId}.");
                return new BaseResult<bool>(HttpStatusCode.BadRequest, "You have already paid for this round.");
            }

            var now = DateTimeOffset.UtcNow;
            var isLate = now > currentRound.CollectionDate;
            
            decimal baseAmount = circle.ContributionAmount.ToDecimal();
            decimal penaltyFromRound = 0m;
            decimal penaltyFromDeposit = 0m;
            decimal totalAmountDue = baseAmount;

            // Apply Penalty Rules
            if (isLate && circle.AllowPenalty)
            {
                var penaltyAmount = circle.PenaltyAmount.ToDecimal();
                if (circle.PenaltyAmountFrom == PenaltyAmountFrom.Round)
                {
                    penaltyFromRound = penaltyAmount;
                    totalAmountDue += penaltyFromRound;
                }
                else
                {
                    penaltyFromDeposit = penaltyAmount;
                }
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            bool paid = await _identityService.DebitBalanceAsync(member.UserPublicId!, totalAmountDue, cancellationToken);

            if (!paid)
            {
                op.Warning($"Insufficient balance for user {request.UserPublicId}. Required: {totalAmountDue}.");
                return new BaseResult<bool>(HttpStatusCode.BadRequest, $"Insufficient balance. You need ₦{totalAmountDue:N0}.");
            }

            // Handle Deposit Penalty deduction
            if (penaltyFromDeposit > 0)
            {
                decimal deducted = Math.Min(member.DepositHeld.ToDecimal(), penaltyFromDeposit);
                member.DepositHeld = Money.FromNaira(member.DepositHeld.ToDecimal() - deducted);
            }

            var paymentRef = $"manual-pay-{circle.Id}-{currentRound.RoundNumber}-{member.Id}-{Guid.NewGuid():N}";

            if (contrib == null)
            {
                contrib = new Contribution
                {
                    CircleRoundId = currentRound.Id,
                    CircleMemberId = member.Id,
                    Amount = Money.FromNaira(totalAmountDue),
                    PenaltyApplied = Money.FromNaira(penaltyFromRound + penaltyFromDeposit),
                    Status = ContributionStatus.Collected,
                    CollectedAt = now,
                    PaymentReference = paymentRef
                };
                await _unitOfWork.AddAsync(contrib, cancellationToken);
            }
            else
            {
                contrib.Status = ContributionStatus.Collected;
                contrib.CollectedAt = now;
                contrib.Amount = Money.FromNaira(totalAmountDue);
                contrib.PenaltyApplied = Money.FromNaira(penaltyFromRound + penaltyFromDeposit);
                contrib.FailureReason = null;
                contrib.PaymentReference = paymentRef;
            }

            // Un-default the member
            if (member.Status == CircleMemberStatus.Defaulted)
            {
                member.Status = CircleMemberStatus.Active;
            }

            currentRound.PooledAmount = Money.FromNaira(currentRound.PooledAmount.ToDecimal() + totalAmountDue);

            // Create Transaction Log
            await _unitOfWork.AddAsync(new Transaction
            {
                UserPublicId = member.UserPublicId!,
                WalletId = null,
                Title = $"Circle Contribution: {circle.Name} (Round {currentRound.RoundNumber})",
                Amount = Money.FromNaira(totalAmountDue),
                Type = TransactionType.Debit,
                Status = TransactionStatus.Completed,
                Reference = paymentRef,
                CompletedAt = now
            }, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            // Success Log
            op.Success($"User {request.UserPublicId} manually paid contribution for Circle {circle.Id}, Round {currentRound.RoundNumber}. Amount: {totalAmountDue}, Total Penalty Applied: {penaltyFromRound + penaltyFromDeposit}");

            return new BaseResult<bool>(HttpStatusCode.OK, "Payment successful.", true);
        }
    }
}