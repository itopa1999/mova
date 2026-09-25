using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class ProcessScheduledReleasesJob
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessScheduledReleasesJob> _logger;
    private readonly IWalletRuleService _walletRuleService;
    private readonly INotificationQueue _notificationQueue;
    private readonly IRenewalService _renewalService;
    private readonly ICacheService _cache;
    private readonly IIdentityService _identityService;

    public ProcessScheduledReleasesJob(
        ApplicationDbContext context,
        ILogger<ProcessScheduledReleasesJob> logger,
        IWalletRuleService walletRuleService,
        INotificationQueue notificationQueue,
        IRenewalService renewalService,
        ICacheService cache,
        IIdentityService identityService)
    {
        _context = context;
        _logger = logger;
        _walletRuleService = walletRuleService;
        _notificationQueue = notificationQueue;
        _renewalService = renewalService;
        _cache = cache;
        _identityService = identityService;
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

        if (releaseIds.Count == 0)
        {
            op.Success("No pending schedule to release.");
            return;
        }

        foreach (var releaseId in releaseIds)
        {
            await ProcessReleaseAsync(releaseId, cancellationToken);
        }
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
        PayoutDestination? releasedDestination = null;

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

            wallet.LockedAmount -= scheduledRelease.Amount;
            wallet.TotalReleasedAmount += scheduledRelease.Amount;

            if (wallet.PayoutDestination == PayoutDestination.Wallet)
            {
                wallet.UnusedAmount += wallet.AvailableAmount;
                wallet.AvailableAmount = scheduledRelease.Amount;
                op.Success("Release kept in wallet available balance.");
            }
            else if (wallet.PayoutDestination == PayoutDestination.Bank
                     || wallet.PayoutDestination == PayoutDestination.Main)
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
                        BankAccountId = wallet.PayoutDestination == PayoutDestination.Bank
                            ? wallet.BankAccountId
                            : null,
                        Destination = wallet.PayoutDestination,
                        Amount = scheduledRelease.Amount,
                        Fee = Money.FromNaira(0),
                        NetAmount = scheduledRelease.Amount,
                        Reference = payoutReference,
                        Provider = null,
                        ProviderReference = null,
                        FailedAttempts = 0,
                        Status = PayoutStatus.Pending,
                        InitiatedAt = DateTimeOffset.UtcNow,
                    };

                    await _context.Set<Payout>().AddAsync(payout, cancellationToken);
                }

                op.Success($"Release queued as {wallet.PayoutDestination} payout.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported payout destination: {wallet.PayoutDestination}");
            }

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

            var inAppDestination = wallet.PayoutDestination switch
            {
                PayoutDestination.Bank =>
                    "and is on its way to your linked bank account.",
                PayoutDestination.Wallet =>
                    "and is now available in your wallet balance.",
                PayoutDestination.Main =>
                    "and has been added to your main MOVA balance.",
                _ => "and has been released."
            };

            var notification = new AppNotification
            {
                UserPublicId = wallet.UserPublicId,
                Type = NotificationType.Release,
                Title = $"{wallet.Name} schedule released",
                Message = $"₦{scheduledRelease.Amount.ToDecimal():N0} has been released from your {wallet.Name} wallet {inAppDestination}",
                IsRead = false,
                ActionUrl = $"/wallet/{wallet.Id}",
                Metadata = null,
            };

            await _context.Set<AppNotification>().AddAsync(notification, cancellationToken);

            scheduledRelease.Status = ReleaseStatus.Released;
            scheduledRelease.ReleasedAt = DateTimeOffset.UtcNow;

            await TryThresholdRenewalAsync(
                wallet,
                cancellationToken);

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
            releasedDestination = wallet.PayoutDestination;
            wasProcessed = true;

            try
            {
                await _cache.DeletePrefixAsync(
                    CacheKeys.NotificationsPrefix(wallet.UserPublicId));
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(
                    cacheEx,
                    "Failed to invalidate notification cache for user {UserPublicId}.",
                    wallet.UserPublicId);
            }

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

        if (!wasProcessed || releasedDestination is null)
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
                releaseId,
                releasedDestination.Value,
                cancellationToken);
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
        long scheduledReleaseId,
        PayoutDestination payoutDestination,
        CancellationToken cancellationToken)
    {
        // ─── Load user via identity service (cache-backed) ───
        var user = await _identityService.GetByIdentifierAsync(
            userPublicId,
            cancellationToken);

        if (user is null)
        {
            _logger.LogWarning(
                "Skipped email for scheduled release {ScheduledReleaseId} — user {UserPublicId} not found.",
                scheduledReleaseId,
                userPublicId);
            return;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "Skipped email for scheduled release {ScheduledReleaseId} — no user email on record.",
                scheduledReleaseId);
            return;
        }

        if (!user.NotifyReleaseAlerts)
        {
            _logger.LogInformation(
                "Skipped email for scheduled release {ScheduledReleaseId} — user {UserPublicId} has release alerts disabled.",
                scheduledReleaseId,
                userPublicId);
            return;
        }

        var emailSubject = payoutDestination switch
        {
            PayoutDestination.Bank =>
                $"Your {walletName} release is on the way",
            PayoutDestination.Wallet =>
                $"Your {walletName} release is now available",
            PayoutDestination.Main =>
                $"Your {walletName} release has been added to your main balance",
            _ => $"Your {walletName} release"
        };

        var emailMessage = payoutDestination switch
        {
            PayoutDestination.Bank =>
                $"₦{amount:N0} has been released from your {walletName} wallet. " +
                $"The money is on its way to your linked bank account and should " +
                $"arrive within a few minutes. MOVA will handle the next release on schedule.",

            PayoutDestination.Wallet =>
                $"₦{amount:N0} has been released from your {walletName} wallet. " +
                $"The money is now available in your wallet balance and you can " +
                $"withdraw it whenever you like. MOVA will handle the next release on schedule.",

            PayoutDestination.Main =>
                $"₦{amount:N0} has been released from your {walletName} wallet. " +
                $"The money is being added to your main MOVA balance. You can spend " +
                $"it inside MOVA — please note it cannot be withdrawn to a bank account. " +
                $"MOVA will handle the next release on schedule.",

            _ =>
                $"₦{amount:N0} has been released from your {walletName} wallet."
        };

        try
        {
            _notificationQueue.QueueNotificationEmail(
                user.FirstName,
                user.Email,
                emailMessage,
                emailSubject);
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
        if (wallet.LockedAmount.MinorUnits <= 0 && wallet.TotalReleasedAmount.MinorUnits > 0)
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

            var outcome = await _renewalService.TryRenewWalletAsync(
                wallet.Id,
                RenewalTriggerType.OnCompletion,
                cancellationToken);

            if (outcome.Executed)
            {
                op.Success("Wallet completed — renewal executed.");
                return;
            }

            op.Success(
                "Target amount reached. No more releases will be scheduled. " +
                $"Renewal: {(outcome.Skipped ? outcome.SkipReason : "not executed")}");

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
                     && x.Status == ReleaseStatus.Scheduled,
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
                Status = ReleaseStatus.Scheduled,
                ReleasedAt = null
            },
            cancellationToken);
    }

    private async Task TryThresholdRenewalAsync(
        Wallet wallet,
        CancellationToken cancellationToken)
    {
        var policy = await _context.Set<RenewalPolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.WalletId == wallet.Id
                     && p.IsEnabled
                     && p.Status == RenewalStatus.Active
                     && p.TriggerType == RenewalTriggerType.OnThreshold,
                cancellationToken);

        if (policy is null)
            return;

        var thresholdMinorUnits = policy.TriggerAmount.MinorUnits;

        if (thresholdMinorUnits <= 0)
            return;

        if (wallet.LockedAmount.MinorUnits > thresholdMinorUnits)
            return;

        using var op = OperationLogger.Start(
            _logger,
            "TryThresholdRenewal",
            ("WalletId", wallet.Id));

        op.Success(
            $"Threshold hit — locked ₦{wallet.LockedAmount.ToDecimal():N0} " +
            $"≤ trigger ₦{policy.TriggerAmount.ToDecimal():N0}");

        await _renewalService.TryRenewWalletAsync(
            wallet.Id,
            RenewalTriggerType.OnThreshold,
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