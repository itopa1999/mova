using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Jobs;

public sealed class ProcessScheduledReleasesJob
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessScheduledReleasesJob> _logger;

    public ProcessScheduledReleasesJob(
        ApplicationDbContext context,
        ILogger<ProcessScheduledReleasesJob> logger)
    {
        _context = context;
        _logger = logger;
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
            scheduledRelease.Status = ReleaseStatus.Failed;
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (wallet.LockedAmount.MinorUnits < scheduledRelease.Amount.MinorUnits)
        {
            scheduledRelease.Status = ReleaseStatus.Failed;
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

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Scheduled release {ScheduledReleaseId} processed for wallet {WalletId}",
            scheduledRelease.Id,
            wallet.Id);
    }
}
