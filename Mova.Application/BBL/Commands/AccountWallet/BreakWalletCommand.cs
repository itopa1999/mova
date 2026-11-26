using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;
using Mova.Shared.Constants;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class BreakWalletCommand
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long WalletId { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<object>>
    {
        private const decimal BreakFeePercentage = 0.02m;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly INotificationQueue _notificationQueue;
        private readonly IPinChallenge _pinChallenge;
        private readonly IIdentityService _identityService;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge,
            IIdentityService identityService)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
            _identityService = identityService;
        }

        public async Task<BaseResult<object>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "BreakWallet",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<object>(HttpStatusCode.BadRequest, "User identity is required.");
            }

            var user = await _unitOfWork.Query<User>()
                .FirstOrDefaultAsync(x => x.PublicId == request.UserPublicId, cancellationToken);

            if (user is null)
                return new BaseResult<object>(HttpStatusCode.Unauthorized, "User not found.");

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId, PinChallengeType.BreakWallet, cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<object>(HttpStatusCode.BadRequest, challengeMessage);
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Warning("Wallet not found.");
                return new BaseResult<object>(HttpStatusCode.NotFound, "Wallet not found.");
            }

            if (wallet.Status != WalletStatus.Active)
            {
                op.Warning($"Wallet is not active. Status: {wallet.Status}");
                return new BaseResult<object>(HttpStatusCode.BadRequest, "Only an active wallet can be broken.");
            }

            var lockedAmount = wallet.LockedAmount.ToDecimal();
            var availableAmount = wallet.AvailableAmount.ToDecimal();

            if (lockedAmount <= 0 && availableAmount <= 0)
            {
                op.Warning("Wallet has no funds to return.");
                return new BaseResult<object>(HttpStatusCode.BadRequest, "This wallet has no funds to return.");
            }

            var breakFeeDecimal = Math.Round(lockedAmount * BreakFeePercentage, 2, MidpointRounding.ToEven);
            var breakFeeMoney = Money.FromNaira(breakFeeDecimal);
            
            var netLockedAmount = wallet.LockedAmount - breakFeeMoney;
            var amountToReturn = netLockedAmount + wallet.AvailableAmount;

            if (amountToReturn.MinorUnits <= 0)
            {
                op.Warning($"Amount after break fee is invalid. Net Locked: {netLockedAmount.ToDecimal():N2}, Available: {availableAmount:N2}");
                return new BaseResult<object>(HttpStatusCode.BadRequest, "The amount available after the break fee is invalid.");
            }

            var reference = $"wallet_break_{wallet.Id:D12}".ToLowerInvariant();

            var alreadyProcessed = await _unitOfWork.Query<Transaction>()
                .AnyAsync(x => x.Reference == reference, cancellationToken);

            if (alreadyProcessed)
            {
                op.Warning($"Wallet break already processed. Reference: {reference}");
                return new BaseResult<object>(HttpStatusCode.BadRequest, "This wallet break has already been processed.");
            }

            var scheduledReleases = await _unitOfWork.Query<ScheduledRelease>()
                .Where(x => x.WalletId == wallet.Id &&
                           (x.Status == ReleaseStatus.Scheduled || x.Status == ReleaseStatus.Processing))
                .ToListAsync(cancellationToken);

            foreach (var scheduledRelease in scheduledReleases)
            {
                scheduledRelease.Status = ReleaseStatus.Cancelled;
            }

            var breakTransaction = new Transaction
            {
                UserPublicId = request.UserPublicId,
                Title = $"Wallet broken: {wallet.Name}",
                Amount = amountToReturn,
                Type = TransactionType.Refund,
                Status = TransactionStatus.Completed,
                Reference = reference,
                CompletedAt = DateTimeOffset.UtcNow
            };

            if (wallet.PayoutDestination == PayoutDestination.Main)
            {
                var credited = await _identityService.CreditBalanceAsync(
                    user, 
                    amountToReturn.ToDecimal(), 
                    cancellationToken);

                if (!credited)
                {
                    op.Error("Failed to credit main MOVA balance.");
                    return new BaseResult<object>(HttpStatusCode.InternalServerError, "Failed to credit your MOVA balance. Please try again.");
                }

                breakTransaction.WalletId = null; 
                wallet.AvailableAmount = Money.FromNaira(0); 

                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    _notificationQueue.QueueTransactionEmail(
                        request.FirstName,
                        request.Email,
                        amountToReturn.ToDecimal().ToString("N2"),
                        EmailTemplateTransaction.Credit,
                        "MOVA Main Balance",
                        reference,
                        DateTimeOffset.UtcNow.ToString("MMM dd, yyyy · HH:mm")
                    );
                }
            }
            else
            {
                breakTransaction.WalletId = wallet.Id;
                wallet.AvailableAmount = amountToReturn; 

                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    _notificationQueue.QueueNotificationEmail(
                        request.FirstName,
                        request.Email,
                        $"# Your {wallet.Name} wallet has been broken\n\n" +
                        $"**₦{amountToReturn.ToDecimal():N0} is on its way to your linked bank account.**\n\n" +
                        $"- A break fee of **₦{breakFeeMoney.ToDecimal():N0}** was applied.\n" +
                        $"- The remaining **₦{amountToReturn.ToDecimal():N0}** is being returned to you.\n" +
                        "- This should arrive within a few minutes.",
                        $"Your {wallet.Name} wallet has been broken");
                }
            }

            await _unitOfWork.AddAsync(breakTransaction, cancellationToken);

            wallet.LockedAmount = Money.FromNaira(0);
            wallet.Status = WalletStatus.Broken;

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var amountReturned = amountToReturn.ToDecimal();
            var breakFee = breakFeeMoney.ToDecimal();

            op.Success($"Wallet broken successfully. Amount returned: ₦{amountReturned:N2}, Break fee: ₦{breakFee:N2}");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                $"{wallet.Name} wallet broken",
                wallet.PayoutDestination == PayoutDestination.Main 
                    ? $"₦{amountReturned:N0} has been added to your MOVA balance."
                    : $"₦{amountReturned:N0} is now available in your wallet.",
                $"/dashboard/scheduled/wallet/{wallet.Id}",
                null,
                CancellationToken.None);

            return new BaseResult<object>(
                HttpStatusCode.OK,
                $"Wallet broken successfully. ₦{amountReturned:N2} has been returned.",
                new { notification = true });
        }
    }
}