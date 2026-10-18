using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

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

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
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
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "User identity is required.");
            }

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.BreakWallet,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId &&
                         x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Warning("Wallet not found.");
                return new BaseResult<object>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Status != WalletStatus.Active)
            {
                op.Warning($"Wallet is not active. Status: {wallet.Status}");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Only an active wallet can be broken.");
            }

            var totalWalletAmount = wallet.LockedAmount + wallet.AvailableAmount;

            if (totalWalletAmount.MinorUnits <= 0)
            {
                op.Warning("Wallet has no funds to return.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "This wallet has no funds to return.");
            }

            var breakFeeMoney = Money.FromNaira(
                Math.Round(
                    wallet.LockedAmount.ToDecimal() * BreakFeePercentage,
                    2,
                    MidpointRounding.ToEven));

            var amountToReturn = totalWalletAmount - breakFeeMoney;

            if (amountToReturn.MinorUnits <= 0)
            {
                op.Warning(
                    $"Amount after break fee is invalid. " +
                    $"Total: {totalWalletAmount.ToDecimal():N2}, " +
                    $"Fee: {breakFeeMoney.ToDecimal():N2}");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "The amount available after the break fee is invalid.");
            }

            var reference = $"wallet-break:{wallet.Id}";

            var alreadyProcessed = await _unitOfWork.Query<Transaction>()
                .AnyAsync(x => x.Reference == reference, cancellationToken);

            if (alreadyProcessed)
            {
                op.Warning($"Wallet break already processed. Reference: {reference}");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "This wallet break has already been processed.");
            }

            var scheduledReleases = await _unitOfWork.Query<ScheduledRelease>()
                .Where(x =>
                    x.WalletId == wallet.Id &&
                    (x.Status == ReleaseStatus.Scheduled ||
                     x.Status == ReleaseStatus.Processing))
                .ToListAsync(cancellationToken);

            foreach (var scheduledRelease in scheduledReleases)
            {
                scheduledRelease.Status = ReleaseStatus.Cancelled;
            }

            var breakTransaction = new Transaction
            {
                UserPublicId = request.UserPublicId,
                WalletId = wallet.Id,
                Title = "Wallet Broken",
                Amount = amountToReturn,
                Type = TransactionType.Refund,
                Status = TransactionStatus.Processing,
                Reference = reference,
            };

            await _unitOfWork.AddAsync(breakTransaction, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.AddAsync(
                new LedgerEntry
                {
                    WalletId = wallet.Id,
                    TransactionId = breakTransaction.Id,
                    Amount = amountToReturn,
                    IsCredit = false,
                },
                cancellationToken);

            var feeTransaction = new Transaction
            {
                UserPublicId = request.UserPublicId,
                WalletId = wallet.Id,
                Title = "Wallet Break Fee",
                Amount = breakFeeMoney,
                Type = TransactionType.Fee,
                Status = TransactionStatus.Completed,
                Reference = $"wallet-break-fee:{wallet.Id}",
                CompletedAt = DateTimeOffset.UtcNow,
            };

            await _unitOfWork.AddAsync(feeTransaction, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.AddAsync(
                new LedgerEntry
                {
                    WalletId = wallet.Id,
                    TransactionId = feeTransaction.Id,
                    Amount = breakFeeMoney,
                    IsCredit = false,
                },
                cancellationToken);

            wallet.LockedAmount = Money.FromNaira(0);
            wallet.AvailableAmount = Money.FromNaira(0);
            wallet.Status = WalletStatus.Broken;

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var amountReturned = amountToReturn.ToDecimal();
            var breakFee = breakFeeMoney.ToDecimal();

            op.Success(
                $"Wallet broken successfully. " +
                $"Amount returned: ₦{amountReturned:N2}, " +
                $"Break fee: ₦{breakFee:N2}");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                $"{wallet.Name} wallet broken",
                $"₦{amountReturned:N0} is being returned to your linked bank " +
                $"account within a few minutes.",
                $"/dashboard/scheduled/wallet/{wallet.Id}",
                null,
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    request.FirstName,
                    request.Email,
                    $"₦{amountReturned:N0} is on its way to your linked bank " +
                    $"account and should arrive within a few minutes. " +
                    $"A break fee of ₦{breakFee:N0} was applied, and the " +
                    $"remaining ₦{amountReturned:N0} is being returned to you.",
                    $"Your {wallet.Name} wallet has been broken");
            }

            return new BaseResult<object>(
                HttpStatusCode.OK,
                $"Wallet broken successfully. " +
                $"₦{amountReturned:N2} is being returned " +
                $"to your linked bank account.",
                new { notification = true });
        }
    }
}