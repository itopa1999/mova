using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class ExpirePendingTrusteesJob
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ExpirePendingTrusteesJob> _logger;
    private readonly IIdentityService _identityService;
    private readonly INotificationQueue _notificationQueue;

    public ExpirePendingTrusteesJob(
        ApplicationDbContext context,
        ILogger<ExpirePendingTrusteesJob> logger,
        IIdentityService identityService,
        INotificationQueue notificationQueue)
    {
        _context = context;
        _logger = logger;
        _identityService = identityService;
        _notificationQueue = notificationQueue;
    }

    [DisableConcurrentExecution(300)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(
            _logger,
            "ExpirePendingTrustees");

        var now = DateTimeOffset.UtcNow;

        var expiredTrusteeIds = await _context.WalletTrustees
            .AsNoTracking()
            .Where(x =>
                x.Status == WalletTrusteeStatus.Pending &&
                x.TokenExpiresAt != null &&
                x.TokenExpiresAt <= now)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (expiredTrusteeIds.Count == 0)
        {
            op.Success("No expired trustee invitations to process.");
            return;
        }

        op.Success(
            $"Found {expiredTrusteeIds.Count} expired trustee invitation(s).");

        var deletedCount = 0;
        var skippedCount = 0;

        foreach (var trusteeId in expiredTrusteeIds)
        {
            try
            {
                var outcome = await ProcessExpiredTrusteeAsync(
                    trusteeId,
                    cancellationToken);

                if (outcome) deletedCount++;
                else skippedCount++;
            }
            catch (Exception ex)
            {
                op.Error(
                    $"Unhandled error expiring trustee {trusteeId}.",
                    ex);
            }
        }

        op.Success(
            $"Expiry run complete. Deleted: {deletedCount}, Skipped: {skippedCount}.");
    }

    private async Task<bool> ProcessExpiredTrusteeAsync(
        long trusteeId,
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(
            _logger,
            "ExpireTrustee",
            ("TrusteeId", trusteeId));

        var trustee = await _context.WalletTrustees
            .AsNoTracking()
            .Where(x => x.Id == trusteeId)
            .Select(x => new
            {
                x.Id,
                x.Email,
                x.Name,
                x.OwnerPublicId,
                x.Status,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (trustee is null)
        {
            op.Success("Trustee already deleted.");
            return false;
        }

        if (trustee.Status != WalletTrusteeStatus.Pending)
        {
            op.Success($"Trustee no longer pending. Status: {trustee.Status}.");
            return false;
        }

        var rowsAffected = await _context.WalletTrustees
            .Where(x =>
                x.Id == trusteeId &&
                x.Status == WalletTrusteeStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);

        if (rowsAffected == 0)
        {
            op.Success("Race lost — trustee accepted or deleted by another process.");
            return false;
        }

        op.Success(
            $"Expired trustee. OwnerId: {trustee.OwnerPublicId}, " +
            $"Email: {trustee.Email}");

        await NotifyOwnerAsync(
            trustee.OwnerPublicId,
            trustee.Email,
            trustee.Name,
            cancellationToken);

        return true;
    }

    private async Task NotifyOwnerAsync(
        string ownerPublicId,
        string trusteeEmail,
        string? trusteeName,
        CancellationToken cancellationToken)
    {
        try
        {
            var trusteeDisplay = string.IsNullOrWhiteSpace(trusteeName)
                ? trusteeEmail
                : trusteeName;

            _notificationQueue.InAppNotificationAsync(
                ownerPublicId,
                NotificationType.Wallet,
                "Trustee invitation expired",
                $"Your trustee invitation to {trusteeDisplay} expired without a response. " +
                $"You can send a new invitation whenever you're ready.",
                "/dashboard/trustees",
                null,
                CancellationToken.None);

            var owner = await _identityService.GetByIdentifierAsync(
                ownerPublicId,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(owner?.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    owner.FirstName ?? string.Empty,
                    owner.Email,
                    $"Your trustee invitation to {trusteeDisplay} expired without a response.\n\n" +
                    $"You can send a new invitation from your Trustees page whenever you're ready.",
                    "Trustee invitation expired");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to notify owner {OwnerId} about trustee expiry.",
                ownerPublicId);
        }
    }
}