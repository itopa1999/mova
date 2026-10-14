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

        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);

        var scheduledRelease = await _context.ScheduledReleases
            .FirstOrDefaultAsync(x => x.Id == releaseId, cancellationToken);

        if (scheduledRelease is null
            || scheduledRelease.Status != ReleaseStatus.Scheduled
        // || scheduledRelease.ScheduledFor > DateTimeOffset.UtcNow)
        )
        {
            op.Success("Release not eligible — skipping.");
            return;
        }

        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(x => x.Id == scheduledRelease.WalletId, cancellationToken);

        if (wallet is null)
        {
            MarkFailure(scheduledRelease);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            op.Warning("Wallet not found — release marked for retry.");
            return;
        }

        if (wallet.Status is WalletStatus.Closed or WalletStatus.Paused)
        {
            scheduledRelease.Status = ReleaseStatus.Paused;
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            op.Success($"Wallet {wallet.Status} — release paused.");
            return;
        }

        if (wallet.Status == WalletStatus.Broken)
        {
            MarkFailure(scheduledRelease);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            op.Warning("Wallet broken — release marked for retry.");
            return;
        }

        if (wallet.Status == WalletStatus.Completed)
        {
            scheduledRelease.Status = ReleaseStatus.Cancelled;
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            op.Success("Wallet completed — release cancelled.");
            return;
        }

        if (wallet.LockedAmount.MinorUnits <= 0)
        {
            MarkFailure(scheduledRelease);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            op.Warning("Wallet has no locked amount — release marked for retry.");
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
            op.Success("Release already processed — idempotent skip.");
            return;
        }

        scheduledRelease.Status = ReleaseStatus.Processing;

        wallet.LockedAmount -= scheduledRelease.Amount;
        wallet.TotalReleasedAmount += scheduledRelease.Amount;
        wallet.ResetAmount += scheduledRelease.Amount;

        if (wallet.PayoutDestination == PayoutDestination.Wallet)
        {
            wallet.AvailableAmount += scheduledRelease.Amount;
        }
        else if (wallet.PayoutDestination is PayoutDestination.Bank
                 or PayoutDestination.Main
                 or PayoutDestination.Utilities)
        {
            var payoutReference = $"scheduled-payout:{scheduledRelease.Id}";

            var payoutAlreadyExists = await _context.Set<Payout>()
                .AnyAsync(x => x.Reference == payoutReference, cancellationToken);

            if (!payoutAlreadyExists)
            {
                await _context.Set<Payout>().AddAsync(
                    new Payout
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
                    },
                    cancellationToken);
            }
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
            CompletedAt = DateTimeOffset.UtcNow,
        };

        await _context.Transactions.AddAsync(releaseTransaction, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        await _context.LedgerEntries.AddAsync(
            new LedgerEntry
            {
                WalletId = wallet.Id,
                TransactionId = releaseTransaction.Id,
                Amount = scheduledRelease.Amount,
                IsCredit = false,
            },
            cancellationToken);

        var inAppDestination = wallet.PayoutDestination switch
        {
            PayoutDestination.Bank =>
                "and is on its way to your linked bank account.",
            PayoutDestination.Wallet =>
                "and is now available in your wallet balance.",
            PayoutDestination.Main =>
                "and has been added to your main MOVA balance.",
            PayoutDestination.Utilities =>
                "and has been used to top up your utility.",
            _ => "and has been released."
        };

        await _context.Set<AppNotification>().AddAsync(
            new AppNotification
            {
                UserPublicId = wallet.UserPublicId,
                Type = NotificationType.Release,
                Title = $"{wallet.Name} schedule released",
                Message = $"₦{scheduledRelease.Amount.ToDecimal():N0} has been released " +
                          $"from your {wallet.Name} wallet {inAppDestination}",
                IsRead = false,
                ActionUrl = $"/wallet/{wallet.Id}",
                Metadata = null,
            },
            cancellationToken);

        scheduledRelease.Status = ReleaseStatus.Released;
        scheduledRelease.ReleasedAt = DateTimeOffset.UtcNow;

        await TryThresholdRenewalAsync(wallet, cancellationToken);
        await EnsureNextScheduledReleaseAsync(wallet, scheduledRelease, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        op.Success(
            $"Release processed. WalletId: {wallet.Id}, " +
            $"Amount: ₦{scheduledRelease.Amount.ToDecimal():N2}, " +
            $"Destination: {wallet.PayoutDestination}.");

        await RunPostCommitSideEffectsAsync(wallet, scheduledRelease, releaseId);
    }

    private async Task RunPostCommitSideEffectsAsync(
        Wallet wallet,
        ScheduledRelease scheduledRelease,
        long releaseId)
    {
        // The release is committed. Any failure here must NOT bubble —
        // Hangfire would retry the whole job and the release would be a
        // no-op on the second run, so the notification would be lost
        // either way. Log and move on.
        try
        {
            await _cache.DeletePrefixAsync(
                CacheKeys.NotificationsPrefix(wallet.UserPublicId));

            await SendReleaseNotificationsAsync(
                wallet.UserPublicId,
                wallet.Name,
                wallet.Id,
                scheduledRelease.Amount.ToDecimal(),
                releaseId,
                wallet.PayoutDestination);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Post-commit side effects failed for scheduled release {ScheduledReleaseId}.",
                releaseId);
        }
    }

    private async Task SendReleaseNotificationsAsync(
        string userPublicId,
        string walletName,
        long walletId,
        decimal amount,
        long scheduledReleaseId,
        PayoutDestination payoutDestination)
    {
        // ─── Load user via identity service (cache-backed) ───
        var user = await _identityService.GetByIdentifierAsync(
            userPublicId,
            CancellationToken.None);

        if (user is null)
        {
            _logger.LogWarning(
                "Skipped email for scheduled release {ScheduledReleaseId} — user not found.",
                scheduledReleaseId);
            return;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "Skipped email for scheduled release {ScheduledReleaseId} — no email on record.",
                scheduledReleaseId);
            return;
        }

        if (!user.NotifyReleaseAlerts)
        {
            _logger.LogInformation(
                "Skipped email for scheduled release {ScheduledReleaseId} — release alerts disabled.",
                scheduledReleaseId);
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
            PayoutDestination.Utilities =>
                $"Your {walletName} release has been used for your utility",
            _ => $"Your {walletName} release",
        };

        var emailMessage = payoutDestination switch
        {
            PayoutDestination.Bank =>
                $"₦{amount:N0} has been released from your {walletName} wallet. " +
                "The money is on its way to your linked bank account and should " +
                "arrive within a few minutes. MOVA will handle the next release on schedule.",

            PayoutDestination.Wallet =>
                $"₦{amount:N0} has been released from your {walletName} wallet. " +
                "The money is now available in your wallet balance and you can " +
                "withdraw it whenever you like. MOVA will handle the next release on schedule.",

            PayoutDestination.Main =>
                $"₦{amount:N0} has been released from your {walletName} wallet. " +
                "The money is being added to your main MOVA balance. You can spend " +
                "it inside MOVA — please note it cannot be withdrawn to a bank account. " +
                "MOVA will handle the next release on schedule.",

            PayoutDestination.Utilities =>
                $"₦{amount:N0} has been released from your {walletName} wallet. " +
                "The money has been used to top up your utility. " +
                "MOVA will handle the next release on schedule.",

            _ =>
                $"₦{amount:N0} has been released from your {walletName} wallet.",
        };

        _notificationQueue.QueueNotificationEmail(
            user.FirstName,
            user.Email,
            emailMessage,
            emailSubject);
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
            .FirstOrDefaultAsync(
                x => x.Id == processedRelease.WalletRuleId,
                cancellationToken);

        if (walletRule is null)
        {
            return;
        }

        var nextRelease = await _walletRuleService.GetNextReleaseAsync(
            walletRule,
            processedRelease.ScheduledFor,
            cancellationToken);

        if (nextRelease is null)
        {
            return;
        }

        var nextScheduledForUtc = nextRelease.ScheduledFor.ToUniversalTime();

        var alreadyScheduled = await _context.ScheduledReleases
            .AnyAsync(
                x => x.WalletRuleId == walletRule.Id
                     && x.ScheduledFor == nextScheduledForUtc
                     && x.Status == ReleaseStatus.Scheduled,
                cancellationToken);

        if (alreadyScheduled)
        {
            return;
        }

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
                ReleasedAt = null,
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
        {
            return;
        }

        var thresholdMinorUnits = policy.TriggerAmount.MinorUnits;

        if (thresholdMinorUnits <= 0)
        {
            return;
        }

        if (wallet.LockedAmount.MinorUnits > thresholdMinorUnits)
        {
            return;
        }

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