using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Jobs;

public sealed class CirclePayoutJob
{
    private readonly ApplicationDbContext _context;
    private readonly ISavingsCircleService _savingsCircleService;
    private readonly INotificationQueue _notificationQueue;
    private readonly ILogger<CirclePayoutJob> _logger;

    public CirclePayoutJob(
        ApplicationDbContext context,
        ISavingsCircleService savingsCircleService,
        INotificationQueue notificationQueue,
        ILogger<CirclePayoutJob> logger)
    {
        _context = context;
        _savingsCircleService = savingsCircleService;
        _notificationQueue = notificationQueue;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 3)]
    public async Task ProcessDuePayoutsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        
        // 1. Find Active circles where the Payout Date is due
        var dueCircles = await _context.SavingsCircles
            .Include(c => c.Members)
            .Where(c => c.Status == SavingsCircleStatus.Active 
                     && c.NextPayoutDate != null 
                     && c.NextPayoutDate <= now)
            .Take(50) // Process in safe batches
            .ToListAsync(cancellationToken);

        if (!dueCircles.Any()) return;

        foreach (var circle in dueCircles)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // 2. Find the current collecting round
                var currentRound = await _context.CircleRounds
                    .FirstOrDefaultAsync(r => r.SavingsCircleId == circle.Id && 
                                             (r.Status == CircleRoundStatus.Collecting || r.Status == CircleRoundStatus.Collected), 
                                             cancellationToken);

                if (currentRound == null)
                {
                    _logger.LogWarning("Active circle {CircleId} has no collecting round. Skipping payout.", circle.Id);
                    continue;
                }

                var recipient = circle.Members.FirstOrDefault(m => m.UserPublicId == currentRound.RecipientUserPublicId);
                if (recipient == null)
                {
                    _logger.LogError("Recipient {UserId} not found in circle {CircleId}.", currentRound.RecipientUserPublicId, circle.Id);
                    continue;
                }

                var expectedTotal = circle.ContributionAmount.ToDecimal() * circle.MemberCount;
                var currentPooled = currentRound.PooledAmount.ToDecimal();

                // 3. Check if the pool is complete before paying out
                if (currentPooled < expectedTotal)
                {
                    _logger.LogInformation("Circle {CircleId} has incomplete funds. Pooled: {Pooled}, Expected: {Expected}", circle.Id, currentPooled, expectedTotal);

                    // Find members who still have Pending contributions
                    var pendingContributions = await _context.Contributions
                        .Include(c => c.CircleMember)
                        .Where(c => c.CircleRoundId == currentRound.Id && c.Status == ContributionStatus.Pending)
                        .ToListAsync(cancellationToken);

                    if (pendingContributions.Any())
                    {
                        var gracePeriodEnd = currentRound.CollectionDate.AddHours(circle.GracePeriodHours);
                        
                        // If the grace period has passed, mark them as Defaulted
                        if (now > gracePeriodEnd)
                        {
                            var defaulterNames = new List<string>();
                            
                            foreach (var contrib in pendingContributions)
                            {
                                // Mark the specific contribution as Failed
                                contrib.Status = ContributionStatus.Failed;
                                contrib.FailureReason = "Grace period elapsed without payment.";
                                
                                var member = contrib.CircleMember;
                                
                                // Mark the member as Defaulted
                                member.Status = CircleMemberStatus.Defaulted;
                                member.MissedContributions += 1;

                                // Apply penalty from deposit if configured
                                if (circle.AllowPenalty && circle.PenaltyAmountFrom == PenaltyAmountFrom.Deposit)
                                {
                                    var penalty = circle.PenaltyAmount.ToDecimal();
                                    member.DepositHeld = Money.FromNaira(Math.Max(0, member.DepositHeld.ToDecimal() - penalty));
                                }

                                defaulterNames.Add(member.DisplayName ?? member.Identifier ?? "Unknown");
                            }

                            await _context.SaveChangesAsync(cancellationToken);

                            // Notify Organizer about the defaulters
                            var organizer = circle.Members.FirstOrDefault(m => m.UserPublicId == circle.OrganizerUserPublicId);
                            if (organizer != null && !string.IsNullOrWhiteSpace(organizer.Identifier))
                            {
                                var defaulterList = string.Join(", ", defaulterNames);
                                var message = $"The payout for Round {currentRound.RoundNumber} in '{circle.Name}' is paused because funds are incomplete. The following members have exceeded the {circle.GracePeriodHours}-hour grace period and are now marked as Defaulted: {defaulterList}.";
                                
                                _notificationQueue.QueueNotificationEmail(
                                    organizer.DisplayName ?? "Organizer",
                                    organizer.Identifier,
                                    $"Action Required: Defaulters in '{circle.Name}'",
                                    message
                                );

                                _notificationQueue.InAppNotificationAsync(
                                    organizer.UserPublicId!,
                                    NotificationType.Wallet,
                                    "Circle Payout Paused",
                                    message,
                                    $"/dashboard/circles/details/{circle.Id}",
                                    null,
                                    cancellationToken
                                );
                            }
                        }
                    }

                    // Skip processing this circle for now. The NextPayoutDate remains in the past, 
                    // so this job will check it again in the next minute.
                    continue;
                }

                // 4. Pay the recipient (Pool is complete)
                var payoutAmount = currentRound.PooledAmount.ToDecimal();
                
                if (payoutAmount > 0)
                {
                    // Find the member's specific Circle Wallet
                    var recipientWallet = await _context.Wallets
                        .FirstOrDefaultAsync(w => w.UserPublicId == recipient.UserPublicId 
                                               && w.SavingsCircleId == circle.Id 
                                               && w.Type == WalletType.Circle, cancellationToken);

                    if (recipientWallet == null)
                    {
                        throw new InvalidOperationException($"Circle wallet not found for recipient {recipient.UserPublicId} in circle {circle.Id}.");
                    }

                    // Safety check: Ensure wallet isn't closed or broken
                    if (recipientWallet.Status == WalletStatus.Broken || recipientWallet.Status == WalletStatus.Closed)
                    {
                        throw new InvalidOperationException($"Cannot process payout. The circle wallet for {recipient.UserPublicId} is {recipientWallet.Status}.");
                    }

                    // Update the Circle Wallet balances directly
                    recipientWallet.AvailableAmount = Money.FromNaira(recipientWallet.AvailableAmount.ToDecimal() + payoutAmount);
                    recipientWallet.TotalReleasedAmount = Money.FromNaira(recipientWallet.TotalReleasedAmount.ToDecimal() + payoutAmount);
                    
                    _context.Wallets.Update(recipientWallet);

                    // Record Transaction & Ledger against the Circle Wallet
                    var transaction = new Transaction
                    {
                        UserPublicId = recipient.UserPublicId!,
                        WalletId = recipientWallet.Id, // Linked to the circle wallet
                        Title = $"Circle Payout: {circle.Name} (Round {currentRound.RoundNumber})",
                        Provider = null,
                        Amount = Money.FromNaira(payoutAmount),
                        Type = TransactionType.Credit,
                        Status = TransactionStatus.Completed,
                        Reference = $"circle-payout-{circle.Id}-{currentRound.RoundNumber}-{Guid.NewGuid():N}",
                        CompletedAt = now
                    };
                    _context.Transactions.Add(transaction);
                    await _context.SaveChangesAsync(cancellationToken);

                    _context.LedgerEntries.Add(new LedgerEntry
                    {
                        WalletId = recipientWallet.Id, // Linked to the circle wallet
                        TransactionId = transaction.Id,
                        Amount = Money.FromNaira(payoutAmount),
                        IsCredit = true
                    });
                }

                // 5. Mark current round as PaidOut
                currentRound.Status = CircleRoundStatus.PaidOut;
                currentRound.PaidOutAmount = currentRound.PooledAmount;
                currentRound.PaidOutAt = now;

                // 6. Update Recipient's stats
                recipient.TotalReceived = Money.FromNaira(recipient.TotalReceived.ToDecimal() + payoutAmount);
                recipient.PayoutReceivedAt = now;

                // 7. Determine if the circle is completed or if a new round should start
                bool isCompleted = currentRound.RoundNumber >= circle.MemberCount;
                
                if (isCompleted)
                {
                    circle.Status = SavingsCircleStatus.Completed;
                    circle.CompletedAt = now;
                    circle.NextCollectionDate = null;
                    circle.NextPayoutDate = null;
                }
                else
                {
                    // Create the Next Round
                    var nextRoundNumber = currentRound.RoundNumber + 1;
                    
                    // Find next recipient based on rotation position
                    var nextRecipient = circle.Members
                        .Where(m => m.Status == CircleMemberStatus.Active)
                        .OrderBy(m => m.RotationPosition)
                        .FirstOrDefault(m => m.RotationPosition == nextRoundNumber);
                    
                    var nextDate = _savingsCircleService.GetNextCollectionDate(circle.Frequency, circle.FrequencyConfig, now);
                    
                    var nextRound = new CircleRound
                    {
                        SavingsCircleId = circle.Id,
                        RoundNumber = nextRoundNumber,
                        CollectionDate = nextDate,
                        PayoutDate = nextDate,
                        RecipientUserPublicId = nextRecipient?.UserPublicId,
                        PooledAmount = Money.FromNaira(0),
                        PaidOutAmount = Money.FromNaira(0),
                        Status = CircleRoundStatus.Collecting,
                        RequiresManualReview = false
                    };
                    
                    _context.CircleRounds.Add(nextRound);
                    circle.NextCollectionDate = nextDate;
                    circle.NextPayoutDate = nextDate;
                }

                // 8. Save all database changes atomically
                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                // 9. Notify Members (Outside the transaction)
                await SendPayoutNotificationsAsync(circle, currentRound, recipient, isCompleted, cancellationToken);

                _logger.LogInformation("Successfully processed payout for circle {CircleId}, round {RoundNumber}.", circle.Id, currentRound.RoundNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process payout for circle {CircleId}.", circle.Id);
                await tx.RollbackAsync(cancellationToken);
            }
        }
    }

    private async Task SendPayoutNotificationsAsync(
        SavingsCircle circle,
        CircleRound paidRound,
        CircleMember recipient,
        bool isCompleted,
        CancellationToken cancellationToken)
    {
        var recipientName = recipient.DisplayName ?? "A member";
        var payoutAmount = paidRound.PooledAmount.ToDecimal();
        var detailsUrl = $"/dashboard/circles/details/{circle.Id}";

        foreach (var member in circle.Members.Where(m => m.Status == CircleMemberStatus.Active))
        {
            if (member.UserPublicId == recipient.UserPublicId)
            {
                // Notify the recipient
                _notificationQueue.InAppNotificationAsync(
                    member.UserPublicId!,
                    NotificationType.Wallet,
                    "You received your payout! 🎉",
                    $"Congratulations! You received your payout of ₦{payoutAmount:N0} for Round {paidRound.RoundNumber} in '{circle.Name}'.",
                    detailsUrl,
                    null,
                    cancellationToken);

                // Optional: Queue Email
                if (!string.IsNullOrWhiteSpace(member.Identifier))
                {
                    _notificationQueue.QueueNotificationEmail(
                        member.DisplayName ?? "Member",
                        member.Identifier,
                        $"You received ₦{payoutAmount:N0} from your '{circle.Name}' circle!",
                        "Your Payout has arrived!");
                }
            }
            else
            {
                // Notify other members
                var message = isCompleted 
                    ? $"Round {paidRound.RoundNumber} of '{circle.Name}' was paid to {recipientName}. The circle is now complete!"
                    : $"Round {paidRound.RoundNumber} of '{circle.Name}' was paid to {recipientName}. Get ready for the next round!";

                _notificationQueue.InAppNotificationAsync(
                    member.UserPublicId!,
                    NotificationType.Wallet,
                    $"Round {paidRound.RoundNumber} Payout Complete",
                    message,
                    detailsUrl,
                    null,
                    cancellationToken);

                // Optional: Queue Email
                if (!string.IsNullOrWhiteSpace(member.Identifier))
                {
                    _notificationQueue.QueueNotificationEmail(
                        member.DisplayName ?? "Member",
                        member.Identifier,
                        message,
                        $"Update from {circle.Name}");
                }
            }
        }
    }
}