using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class ExpireReleaseRequestsJob
{
    private readonly ApplicationDbContext _context;
    private readonly IIdentityService _identityService;
    private readonly INotificationQueue _notificationQueue;
    private readonly ILogger<ExpireReleaseRequestsJob> _logger;

    public ExpireReleaseRequestsJob(
        ApplicationDbContext context,
        IIdentityService identityService,
        INotificationQueue notificationQueue,
        ILogger<ExpireReleaseRequestsJob> logger)
    {
        _context = context;
        _identityService = identityService;
        _notificationQueue = notificationQueue;
        _logger = logger;
    }

    [DisableConcurrentExecution(300)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "ExpireReleaseRequests");

        var now = DateTimeOffset.UtcNow;

        var expiredIds = await _context.ReleaseRequests
            .AsNoTracking()
            .Where(r =>
                r.Status == ReleaseRequestStatus.Pending &&
                r.ExpiresAt <= now)
            .OrderBy(r => r.Id)
            .Select(r => r.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (expiredIds.Count == 0)
        {
            op.Success("No expired release requests.");
            return;
        }

        var processed = 0;

        foreach (var requestId in expiredIds)
        {
            try
            {
                var affected = await _context.ReleaseRequests
                    .Where(r =>
                        r.Id == requestId &&
                        r.Status == ReleaseRequestStatus.Pending)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(x => x.Status, ReleaseRequestStatus.Expired)
                            .SetProperty(x => x.DecidedAt, now)
                            .SetProperty(x => x.ApprovalTokenHash, (byte[]?)null),
                        cancellationToken);

                if (affected == 0) continue;

                var releaseRequest = await _context.ReleaseRequests
                    .Include(r => r.Wallet)
                    .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

                if (releaseRequest?.Wallet is null) continue;

                var wallet = releaseRequest.Wallet;

                wallet.LockedAmount = Money.FromMinorUnits(
                    checked(wallet.LockedAmount.MinorUnits + releaseRequest.Amount.MinorUnits),
                    wallet.LockedAmount.Currency);

                await _context.SaveChangesAsync(cancellationToken);
                processed++;

                // Notifications
                _notificationQueue.InAppNotificationAsync(
                    releaseRequest.RequestedByUserPublicId,
                    NotificationType.Wallet,
                    "Release request expired",
                    $"Your release request of " +
                    $"₦{releaseRequest.Amount.ToDecimal():N0} from \"{wallet.Name}\" " +
                    $"expired without a response. Funds are back in the wallet.",
                    $"/dashboard/trustee/wallet/{wallet.Id}",
                    null,
                    CancellationToken.None);

                var owner = await _identityService.GetByIdentifierAsync(
                    releaseRequest.RequestedByUserPublicId,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(owner?.Email))
                {
                    _notificationQueue.QueueNotificationEmail(
                        owner.FirstName ?? string.Empty,
                        owner.Email,
                        $"Your release request of " +
                        $"₦{releaseRequest.Amount.ToDecimal():N0} from " +
                        $"\"{wallet.Name}\" expired without a response from your " +
                        $"trustee.\n\nFunds are back in your wallet. You can " +
                        $"request again after the cooldown period.",
                        "Release request expired");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expire release request {Id}", requestId);
            }
        }

        op.Success($"Processed {processed} expired release request(s).");
    }
}