using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class ProcessScheduledReleasesJob
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessScheduledReleasesJob> _logger;
    private readonly IWalletRuleService _walletRuleService;
    private readonly INotificationQueue _notificationQueue;

    public ProcessScheduledReleasesJob(
        ApplicationDbContext context,
        ILogger<ProcessScheduledReleasesJob> logger,
        IWalletRuleService walletRuleService,
        INotificationQueue notificationQueue)
    {
        _context = context;
        _logger = logger;
        _walletRuleService = walletRuleService;
        _notificationQueue = notificationQueue;
    }

    [DisableConcurrentExecution(300)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "ProcessScheduledReleases");

        var releaseIds = await _context.ScheduledReleases
            .AsNoTracking()
            .Where(x => x.Status == ReleaseStatus.Scheduled
                        // && x.ScheduledFor <= DateTimeOffset.UtcNow)
            )
            .OrderBy(x => x.ScheduledFor)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var releaseId in releaseIds)
        {
            await ProcessReleaseAsync(releaseId, cancellationToken);
        }

        op.Success($"Processed {releaseIds.Count} scheduled release(s).");
    }

    private async Task ProcessReleaseAsync(
        long releaseId,
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(
            _logger,
            "ProcessScheduledRelease",
            ("ScheduledReleaseId", releaseId));

        string creditedUserPublicId = string.Empty;
        string walletName = string.Empty;
        long walletId = 0;
        decimal releasedAmount = 0m;
        bool wasProcessed = false;

        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);

        try
        {
            var scheduledRelease = await _context.ScheduledReleases
                .FirstOrDefaultAsync(x => x.Id == releaseId, cancellationToken);

            if (scheduledRelease is null
                || scheduledRelease.Status != ReleaseStatus.Scheduled
                // || scheduledRelease.ScheduledFor > DateTimeOffset.UtcNow)
            )
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var wallet = await _context.Wallets
                .FirstOrDefaultAsync(x => x.Id == scheduledRelease.WalletId, cancellationToken);

            if (wallet is null)
            {
                MarkFailure(scheduledRelease);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (wallet.Status is WalletStatus.Closed or WalletStatus.Paused)
            {
                scheduledRelease.Status = ReleaseStatus.Paused;
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (wallet.Status == WalletStatus.Broken)
            {
                MarkFailure(scheduledRelease);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (wallet.Status == WalletStatus.Completed)
            {
                scheduledRelease.Status = ReleaseStatus.Cancelled;
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (wallet.LockedAmount.MinorUnits <= 0)
            {
                MarkFailure(scheduledRelease);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (wallet.LockedAmount.MinorUnits < scheduledRelease.Amount.MinorUnits)
            {
                scheduledRelease.Amount = wallet.LockedAmount;
            }

            var reference = $"scheduled-release:{scheduledRelease.Id}";
            var alreadyProcessed = await _context.Transactions
                .AnyAsync(x => x.Reference == reference, cancellationToken);

            if (alreadyProcessed)
            {
                scheduledRelease.Status = ReleaseStatus.Released;
                scheduledRelease.ReleasedAt ??= DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            scheduledRelease.Status = ReleaseStatus.Processing;
            wallet.UnusedAmount += wallet.AvailableAmount;
            wallet.LockedAmount -= scheduledRelease.Amount;
            wallet.AvailableAmount = scheduledRelease.Amount;
            wallet.TotalReleasedAmount += scheduledRelease.Amount;

            var releaseTransaction = new Transaction
            {
                UserPublicId = wallet.UserPublicId,
                WalletId = wallet.Id,
                Title = "Schedule Released",
                Amount = scheduledRelease.Amount,
                Type = TransactionType.Release,
                Status = TransactionStatus.Completed,
                Reference = reference,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await _context.Transactions.AddAsync(releaseTransaction, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            var ledgerEntry = new LedgerEntry
            {
                WalletId = wallet.Id,
                TransactionId = releaseTransaction.Id,
                Amount = scheduledRelease.Amount,
                IsCredit = false
            };

            await _context.LedgerEntries.AddAsync(ledgerEntry, cancellationToken);

            if (wallet.BankAccountId is null || wallet.BankAccountId <= 0)
            {
                op.Success("Skipped payout: wallet has no linked bank account.");
            }
            else
            {
                var payoutReference = $"scheduled-payout:{scheduledRelease.Id}";

                var payoutAlreadyExists = await _context.Set<Payout>()
                    .AnyAsync(x => x.Reference == payoutReference, cancellationToken);

                if (!payoutAlreadyExists)
                {
                    var payout = new Payout
                    {
                        UserPublicId = wallet.UserPublicId,
                        WalletId = wallet.Id,
                        BankAccountId = wallet.BankAccountId.Value,
                        Amount = scheduledRelease.Amount,
                        Fee = Money.FromNaira(0),
                        NetAmount = scheduledRelease.Amount,
                        Reference = payoutReference,
                        Provider = null,
                        ProviderReference = null,
                        FailedAttempts = 0,
                        Status = PayoutStatus.Pending,
                        InitiatedAt = DateTimeOffset.UtcNow
                    };

                    await _context.Set<Payout>().AddAsync(payout, cancellationToken);
                }
            }

            var notification = new AppNotification
            {
                UserPublicId = wallet.UserPublicId,
                Type = NotificationType.Release,
                Title = $"{wallet.Name} schedule released",
                Message = $"₦{scheduledRelease.Amount.ToDecimal():N0} has been released from",
                IsRead = false,
                ActionUrl = $"/wallet/{wallet.Id}",
                Metadata = null,
            };

            await _context.Set<AppNotification>().AddAsync(notification, cancellationToken);

            scheduledRelease.Status = ReleaseStatus.Released;
            scheduledRelease.ReleasedAt = DateTimeOffset.UtcNow;

            await EnsureNextScheduledReleaseAsync(
                wallet,
                scheduledRelease,
                cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            creditedUserPublicId = wallet.UserPublicId;
            walletName = wallet.Name;
            walletId = wallet.Id;
            releasedAmount = scheduledRelease.Amount.ToDecimal();
            wasProcessed = true;

            op.Success("Scheduled release processed.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);

            op.Fail(
                $"Error processing scheduled release. ScheduledReleaseId: {releaseId}",
                ex);

            throw;
        }

        if (!wasProcessed)
        {
            return;
        }

        try
        {
            await SendReleaseNotificationsAsync(
                creditedUserPublicId,
                walletName,
                walletId,
                releasedAmount,
                releaseId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Notification block failed for scheduled release {ScheduledReleaseId}.",
                releaseId);
        }
    }

    private async Task SendReleaseNotificationsAsync(
        string userPublicId,
        string walletName,
        long walletId,
        decimal amount,
        long scheduledReleaseId)
    {
        var title = $"{walletName} schedule released";

        var inAppMessage =
            $"₦{amount:N0} has been released from your {walletName} wallet " +
            $"and is on its way to your linked bank account.";

        var emailSubject = $"Your {walletName} release is on the way";

        var emailMessage =
            $"₦{amount:N0} has been released from your {walletName} wallet. " +
            $"The money is on its way to your linked bank account and should " +
            $"arrive within a few minutes. MOVA will handle the next release on schedule.";

        try
        {
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.PublicId == userPublicId);

            if (user is null || string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogWarning(
                    "Skipped email for scheduled release {ScheduledReleaseId} — no user email on record.",
                    scheduledReleaseId);
            }
            else
            {
                _notificationQueue.QueueNotificationEmail(
                    user.FirstName,
                    user.Email,
                    emailMessage,
                    emailSubject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Email queue failed for scheduled release {ScheduledReleaseId}.",
                scheduledReleaseId);
        }
    }

    private async Task EnsureNextScheduledReleaseAsync(
        Wallet wallet,
        ScheduledRelease processedRelease,
        CancellationToken cancellationToken)
    {
        if (wallet.TotalReleasedAmount.MinorUnits >= wallet.TargetAmount.MinorUnits)
        {
            if (wallet.Status != WalletStatus.Completed)
            {
                wallet.Status = WalletStatus.Completed;
                wallet.CompletedAt = DateTimeOffset.UtcNow;
            }
            using var op = OperationLogger.Start(
                _logger,
                "EnsureNextScheduledRelease",
                ("WalletId", wallet.Id));
            op.Success("Target amount reached. No more releases will be scheduled.");
            return;
        }

        var walletRule = await _context.Set<WalletRule>()
            .FirstOrDefaultAsync(x => x.Id == processedRelease.WalletRuleId, cancellationToken);

        if (walletRule is null)
            return;

        var nextRelease = await _walletRuleService.GetNextReleaseAsync(
            walletRule,
            processedRelease.ScheduledFor,
            cancellationToken);

        if (nextRelease is null)
            return;

        var nextScheduledForUtc = nextRelease.ScheduledFor.ToUniversalTime();

        var alreadyScheduled = await _context.ScheduledReleases
            .AnyAsync(
                x => x.WalletRuleId == walletRule.Id
                     && x.ScheduledFor == nextScheduledForUtc
                     && x.Status != ReleaseStatus.Cancelled,
                cancellationToken);

        if (alreadyScheduled)
            return;

        var nextAmount = nextRelease.Amount.MinorUnits > wallet.LockedAmount.MinorUnits
            ? wallet.LockedAmount
            : nextRelease.Amount;

        await _context.ScheduledReleases.AddAsync(
            new ScheduledRelease
            {
                WalletId = wallet.Id,
                WalletRuleId = walletRule.Id,
                Amount = nextAmount,
                ScheduledFor = nextRelease.ScheduledFor,
                Status = ReleaseStatus.Scheduled
            },
            cancellationToken);
    }

    private static void MarkFailure(ScheduledRelease scheduledRelease)
    {
        scheduledRelease.FailedAttempts++;
        scheduledRelease.Status = scheduledRelease.FailedAttempts >= 3
            ? ReleaseStatus.Failed
            : ReleaseStatus.Scheduled;
    }
}