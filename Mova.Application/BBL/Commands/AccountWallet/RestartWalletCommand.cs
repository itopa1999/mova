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
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class RestartWalletCommand
{
    public sealed class Command : IRequest<BaseResult<RestartWalletResponseDto>>
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

    public sealed class RestartWalletResponseDto
    {
        public long WalletId { get; init; }
        public DateTimeOffset FirstReleaseDate { get; init; }
        public decimal NewMainBalance { get; init; }
        public bool Notification { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<RestartWalletResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly IWalletRuleService _walletRuleService;
        private readonly INotificationQueue _notificationQueue;
        private readonly IPinChallenge _pinChallenge;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            IWalletRuleService walletRuleService,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _walletRuleService = walletRuleService;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
        }

        public async Task<BaseResult<RestartWalletResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "RestartWallet",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            var wallet = await _unitOfWork.Query<Wallet>()
                .Include(w => w.Rule)
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId
                         && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Warning("Wallet not found.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Status != WalletStatus.Broken
                && wallet.Status != WalletStatus.Completed)
            {
                op.Warning($"Wallet status is not restartable: {wallet.Status}.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Only broken or completed wallets can be restarted.");
            }

            if (wallet.Rule is null)
            {
                op.Warning("Wallet has no rule to re-schedule from.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet has no schedule to restart.");
            }

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.RestartWallet,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            var targetAmount = wallet.TargetAmount.ToDecimal();
            var releaseAmount = wallet.Rule.Amount.ToDecimal();

            if (targetAmount <= 0 || releaseAmount <= 0)
            {
                op.Warning("Wallet has an invalid target or release amount.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet's target or release amount is invalid.");
            }

            var releases = (int)Math.Ceiling(targetAmount / releaseAmount);

            if (releases <= 0)
            {
                op.Warning("Could not determine release count for this wallet.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Unable to determine the wallet's release schedule.");
            }

            var destinationString = wallet.PayoutDestination switch
            {
                PayoutDestination.Bank => "bank",
                PayoutDestination.Wallet => "wallet",
                PayoutDestination.Main => "main",
                _ => "bank",
            };

            var fees = WalletFeeHelper.Calculate(
                targetAmount: targetAmount,
                releaseAmount: releaseAmount,
                payoutDestination: destinationString,
                releases: releases);

            var totalUpfrontCharge = fees.TotalUpfrontCharge;

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var balanceDebited = await _identityService.DebitBalanceAsync(
                request.UserPublicId,
                totalUpfrontCharge,
                cancellationToken);

            if (!balanceDebited)
            {
                op.Warning("Insufficient account balance or user account not found.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Insufficient account balance.");
            }

            var targetMoney = Money.FromNaira(targetAmount);
            var releaseMoney = Money.FromNaira(releaseAmount);

            wallet.Status = WalletStatus.Active;
            wallet.TargetAmount = targetMoney;
            wallet.FundedAmount += targetMoney;
            wallet.LockedAmount += targetMoney;
            wallet.ResetAmount = Money.FromNaira(0);
            wallet.CompletedAt = null;
            wallet.RestartCount += 1;

            var ruleForEndDate = new WalletRule
            {
                Amount = releaseMoney,
                Frequency = wallet.Rule.Frequency,
                FrequencyConfig = wallet.Rule.FrequencyConfig,
                StartDate = DateTimeOffset.UtcNow,
            };

            var cursor = DateTimeOffset.UtcNow.AddTicks(-1);
            DateTimeOffset? finalEndDate = null;

            for (var i = 0; i < releases; i++)
            {
                var next = await _walletRuleService.GetNextReleaseAsync(
                    ruleForEndDate,
                    cursor,
                    cancellationToken);

                if (next is null)
                {
                    break;
                }

                finalEndDate = next.ScheduledFor;
                cursor = next.ScheduledFor;
            }

            if (finalEndDate is null)
            {
                op.Warning("Unable to compute the restart schedule.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Unable to compute the restart schedule.");
            }

            wallet.Rule.StartDate = DateTimeOffset.UtcNow;
            wallet.Rule.EndDate = finalEndDate.Value;

            var pendingReleases = await _unitOfWork.Query<ScheduledRelease>()
                .Where(sr => sr.WalletId == wallet.Id
                             && sr.Status == ReleaseStatus.Scheduled)
                .ToListAsync(cancellationToken);

            foreach (var pr in pendingReleases)
            {
                pr.Status = ReleaseStatus.Processing;
            }

            if (pendingReleases.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var restartTx = new Transaction
            {
                UserPublicId = request.UserPublicId,
                WalletId = wallet.Id,
                Title = "Wallet Restarted",
                Amount = targetMoney,
                Type = TransactionType.Deposit,
                Status = TransactionStatus.Completed,
                Reference = $"wallet-restart:{wallet.Id}:{Guid.NewGuid():N}",
                CompletedAt = DateTimeOffset.UtcNow,
            };

            await _unitOfWork.AddAsync(restartTx, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.AddAsync(
                new LedgerEntry
                {
                    WalletId = wallet.Id,
                    TransactionId = restartTx.Id,
                    Amount = targetMoney,
                    IsCredit = true,
                },
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var feeTx = new Transaction
            {
                UserPublicId = request.UserPublicId,
                WalletId = wallet.Id,
                Title = "Wallet Restart Fee",
                Amount = Money.FromNaira(fees.TotalMovaCharges),
                Type = TransactionType.Fee,
                Status = TransactionStatus.Completed,
                Reference = $"wallet-restart-fee:{wallet.Id}:{Guid.NewGuid():N}",
                CompletedAt = DateTimeOffset.UtcNow,
            };

            await _unitOfWork.AddAsync(feeTx, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var firstRelease = await _walletRuleService.GetNextReleaseAsync(
                wallet.Rule,
                wallet.Rule.StartDate.AddTicks(-1),
                cancellationToken)
                ?? throw new InfrastructureException(
                    "Unable to generate the first release after restart.");

            var firstReleaseAmount = Math.Min(
                firstRelease.Amount.ToDecimal(),
                targetAmount);

            var scheduledRelease = new ScheduledRelease
            {
                WalletId = wallet.Id,
                WalletRuleId = wallet.Rule.Id,
                Amount = Money.FromNaira(firstReleaseAmount),
                ScheduledFor = firstRelease.ScheduledFor,
                Status = ReleaseStatus.Scheduled,
                ReleasedAt = null,
            };

            await _unitOfWork.AddAsync(scheduledRelease, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var firstReleaseDate = firstRelease.ScheduledFor;

            var userAfterDebit = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            var newMainBalance = userAfterDebit?.Balance.ToDecimal() ?? 0m;

            op.Success(
                $"Wallet restarted. WalletId: {wallet.Id}, " +
                $"FirstRelease: {firstReleaseDate:u}, " +
                $"NewBalance: {newMainBalance}");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                $"{wallet.Name} wallet restarted",
                $"Your {wallet.Name} wallet has been restarted. " +
                $"First release of the new cycle is scheduled for " +
                $"{firstReleaseDate:MMM d, yyyy}. " +
                BuildDestinationClause(wallet.PayoutDestination),
                $"/wallet/{wallet.Id}",
                null,
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    request.FirstName,
                    request.Email,
                    $"Your {wallet.Name} wallet has been restarted successfully. " +
                    $"A fresh cycle has begun and the first release is scheduled " +
                    $"for {firstReleaseDate:MMM d, yyyy}. " +
                    BuildDestinationClause(wallet.PayoutDestination),
                    $"{wallet.Name} wallet restarted");
            }

            return new BaseResult<RestartWalletResponseDto>(
                HttpStatusCode.OK,
                "Wallet restarted successfully.",
                new RestartWalletResponseDto
                {
                    WalletId = wallet.Id,
                    FirstReleaseDate = firstReleaseDate,
                    NewMainBalance = newMainBalance,
                    Notification = true,
                });
        }

        private static string BuildDestinationClause(PayoutDestination destination)
            => destination switch
            {
                PayoutDestination.Bank =>
                    "Releases will continue to be sent to your linked bank account.",
                PayoutDestination.Wallet =>
                    "Releases will continue to land in your wallet available balance.",
                PayoutDestination.Main =>
                    "Releases will continue to be added to your main MOVA balance.",
                _ => "Releases will continue on schedule."
            };
    }
}