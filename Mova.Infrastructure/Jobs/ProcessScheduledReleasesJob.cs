using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Jobs;

public sealed class ProcessScheduledReleasesJob
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessScheduledReleasesJob> _logger;
    private readonly IWalletRuleService _walletRuleService;

    public ProcessScheduledReleasesJob(
        ApplicationDbContext context,
        ILogger<ProcessScheduledReleasesJob> logger,
        IWalletRuleService walletRuleService)
    {
        _context = context;
        _logger = logger;
        _walletRuleService = walletRuleService;
    }

    [DisableConcurrentExecution(300)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var releaseIds = await _context.ScheduledReleases
            .AsNoTracking()
            .Where(x => x.Status == ReleaseStatus.Scheduled
                        && x.ScheduledFor <= DateTimeOffset.UtcNow)
            .OrderBy(x => x.ScheduledFor)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var releaseId in releaseIds)
        {
            await ProcessReleaseAsync(releaseId, cancellationToken);
        }
    }

    private async Task ProcessReleaseAsync(
        long releaseId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);

        var scheduledRelease = await _context.ScheduledReleases
            .FirstOrDefaultAsync(x => x.Id == releaseId, cancellationToken);

        if (scheduledRelease is null
            || scheduledRelease.Status != ReleaseStatus.Scheduled
            || scheduledRelease.ScheduledFor > DateTimeOffset.UtcNow)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var wallet = await _context.Wallets
            .FirstOrDefaultAsync(x => x.Id == scheduledRelease.WalletId, cancellationToken);

        if (wallet is null || wallet.Status is WalletStatus.Closed or WalletStatus.Paused)
        {
            MarkFailure(scheduledRelease);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (wallet.LockedAmount.MinorUnits < scheduledRelease.Amount.MinorUnits)
        {
            MarkFailure(scheduledRelease);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
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
            WalletId = wallet.Id,
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

        scheduledRelease.Status = ReleaseStatus.Released;
        scheduledRelease.ReleasedAt = DateTimeOffset.UtcNow;

        await EnsureNextScheduledReleaseAsync(
            wallet,
            scheduledRelease,
            cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Scheduled release {ScheduledReleaseId} processed for wallet {WalletId}",
            scheduledRelease.Id,
            wallet.Id);
    }

    private async Task EnsureNextScheduledReleaseAsync(
        Wallet wallet,
        ScheduledRelease processedRelease,
        CancellationToken cancellationToken)
    {
        if (wallet.TotalReleasedAmount.MinorUnits >= wallet.TargetAmount.MinorUnits)
        {
            _logger.LogInformation(
                "Target amount reached for wallet {WalletId}. No more releases will be scheduled.",
                wallet.Id);
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

        var alreadyScheduled = await _context.ScheduledReleases
            .AnyAsync(
                x => x.WalletRuleId == walletRule.Id
                     && x.ScheduledFor == nextRelease.ScheduledFor
                     && x.Status != ReleaseStatus.Cancelled,
                cancellationToken);

        if (alreadyScheduled)
            return;

        await _context.ScheduledReleases.AddAsync(
            new ScheduledRelease
            {
                WalletId = wallet.Id,
                WalletRuleId = walletRule.Id,
                Amount = nextRelease.Amount,
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
