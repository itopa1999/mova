using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.Shared;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
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

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            IWalletRuleService walletRuleService,
            INotificationQueue notificationQueue)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _walletRuleService = walletRuleService;
            _notificationQueue = notificationQueue;
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
                op.Fail("Wallet not found.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            var status = wallet.Status;

            if (status != WalletStatus.Broken && status != WalletStatus.Completed)
            {
                op.Fail($"Wallet status is not restartable: {status}.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Only broken or completed wallets can be restarted.");
            }

            if (wallet.Rule is null)
            {
                op.Fail("Wallet has no rule to re-schedule from.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet has no schedule to restart.");
            }

            // ─── Compute the fee for restarting this wallet ─────
            // Same helper CreateWallet uses. Charge is based on the
            // wallet's target amount (the fresh principal going back in)
            // and the configured per-release amount.
            var targetAmount = wallet.TargetAmount.ToDecimal();
            var releaseAmount = wallet.Rule.Amount.ToDecimal();

            if (targetAmount <= 0 || releaseAmount <= 0)
            {
                op.Fail("Wallet has an invalid target or release amount.");
                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet's target or release amount is invalid.");
            }

            var releases = (int)Math.Ceiling(targetAmount / releaseAmount);

            if (releases <= 0)
            {
                op.Fail("Could not determine release count for this wallet.");
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

            op.Success(
                $"Restart fee computed — releases: {releases}, " +
                $"creation: {fees.CreationFee}, payout: {fees.TotalPayoutFee}, " +
                $"total charge: {totalUpfrontCharge}");

            long walletId = wallet.Id;
            DateTimeOffset firstReleaseDate = default;
            decimal newMainBalance = 0m;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // ─── Debit user's main balance for target + fees ───
                var balanceDebited = await _identityService.DebitBalanceAsync(
                    request.UserPublicId,
                    totalUpfrontCharge,
                    cancellationToken);

                if (!balanceDebited)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail("Insufficient account balance or user account not found.");

                    return new BaseResult<RestartWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Insufficient account balance.");
                }

                var targetMoney = Money.FromNaira(targetAmount);
                var releaseMoney = Money.FromNaira(releaseAmount);

                // ─── Reset wallet to a fresh active cycle ──────────
                // Use += rather than = so we don't clobber any principal
                // that automation may have already injected.
                wallet.Status = WalletStatus.Active;
                wallet.TargetAmount = targetMoney;
                wallet.FundedAmount += targetMoney;
                wallet.LockedAmount += targetMoney;
                wallet.CompletedAt = null;
                wallet.RestartCount += 1;

                // ─── Recompute rule end date from the preview ──────
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

                    if (next is null) break;

                    finalEndDate = next.ScheduledFor;
                    cursor = next.ScheduledFor;
                }

                if (finalEndDate is null)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail("Unable to compute the restart schedule.");
                    return new BaseResult<RestartWalletResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Unable to compute the restart schedule.");
                }

                wallet.Rule.StartDate = DateTimeOffset.UtcNow;
                wallet.Rule.EndDate = finalEndDate.Value;

                // ─── Mark any un-released scheduled releases ───────
                // We don't delete them — they're parked in Processing
                // so the release engine skips them. A separate task
                // will clean them up / reclassify them later.
                var pendingReleases = await _unitOfWork.Query<ScheduledRelease>()
                    .Where(sr => sr.WalletId == wallet.Id
                                 && sr.Status == ReleaseStatus.Scheduled)
                    .ToListAsync(cancellationToken);

                if (pendingReleases.Count > 0)
                {
                    foreach (var pr in pendingReleases)
                    {
                        pr.Status = ReleaseStatus.Processing;
                    }

                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    op.Success(
                        $"Marked {pendingReleases.Count} pending release(s) as Processing.");
                }

                // ─── Restart transaction (deposit) ─────────────────
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

                var ledger = new LedgerEntry
                {
                    WalletId = wallet.Id,
                    TransactionId = restartTx.Id,
                    Amount = targetMoney,
                    IsCredit = true,
                };

                await _unitOfWork.AddAsync(ledger, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // ─── Restart fee transaction ───────────────────────
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

                // ─── Schedule the first release of the new cycle ───
                var firstRelease = await _walletRuleService.GetNextReleaseAsync(
                    wallet.Rule,
                    wallet.Rule.StartDate.AddTicks(-1),
                    cancellationToken);

                if (firstRelease is null)
                {
                    throw new InvalidOperationException(
                        "Unable to generate the first release after restart.");
                }

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
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                firstReleaseDate = firstRelease.ScheduledFor;

                var userAfterDebit = await _identityService.GetByIdentifierAsync(
                    request.UserPublicId,
                    cancellationToken);

                newMainBalance = userAfterDebit?.Balance.ToDecimal() ?? 0m;

                op.Success(
                    $"Wallet restarted. WalletId: {wallet.Id}, " +
                    $"FirstRelease: {firstRelease.ScheduledFor:u}, " +
                    $"NewBalance: {newMainBalance}");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                _logger.LogError(
                    ex,
                    "Error restarting wallet {WalletId} for user {UserPublicId}.",
                    request.WalletId,
                    request.UserPublicId);

                op.Fail($"Error restarting wallet: {ex.Message}");

                return new BaseResult<RestartWalletResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while restarting the wallet.");
            }

            try
            {
                await SendWalletRestartedNotificationsAsync(
                    request.UserPublicId,
                    request.Email,
                    request.FirstName,
                    walletId,
                    wallet.Name,
                    firstReleaseDate,
                    wallet.PayoutDestination);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for restarted wallet {WalletId}.",
                    walletId);
            }

            return new BaseResult<RestartWalletResponseDto>(
                HttpStatusCode.OK,
                "Wallet restarted successfully.",
                new RestartWalletResponseDto
                {
                    WalletId = walletId,
                    FirstReleaseDate = firstReleaseDate,
                    NewMainBalance = newMainBalance,
                    Notification = true,
                });
        }

        private async Task SendWalletRestartedNotificationsAsync(
            string userPublicId,
            string email,
            string firstName,
            long walletId,
            string walletName,
            DateTimeOffset firstReleaseDate,
            PayoutDestination payoutDestination)
        {
            var title = $"{walletName} wallet restarted";

            var destinationClause = payoutDestination switch
            {
                PayoutDestination.Bank =>
                    "Releases will continue to be sent to your linked bank account.",
                PayoutDestination.Wallet =>
                    "Releases will continue to land in your wallet available balance.",
                PayoutDestination.Main =>
                    "Releases will continue to be added to your main MOVA balance.",
                _ => "Releases will continue on schedule."
            };

            var inAppMessage =
                $"Your {walletName} wallet has been restarted. " +
                $"First release of the new cycle is scheduled for " +
                $"{firstReleaseDate:MMM d, yyyy}. {destinationClause}";

            var emailSubject = $"{walletName} wallet restarted";

            var emailMessage =
                $"Your {walletName} wallet has been restarted successfully. " +
                $"A fresh cycle has begun and the first release is scheduled " +
                $"for {firstReleaseDate:MMM d, yyyy}. {destinationClause}";

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.Wallet,
                    title,
                    inAppMessage,
                    $"/wallet/{walletId}",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for restarted wallet {WalletId}.",
                    walletId);
            }

            try
            {
                _notificationQueue.QueueNotificationEmail(
                    firstName,
                    email,
                    emailMessage,
                    emailSubject);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Email queue failed for restarted wallet {WalletId}.",
                    walletId);
            }
        }
    }
}