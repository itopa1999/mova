using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Jobs.CircleJob;

public sealed class CirclePayoutJob
{
    private readonly ApplicationDbContext _context;
    private readonly ISavingsCircleService _savingsCircleService;
    private readonly INotificationQueue _notificationQueue;
    private readonly IIdentityService _identityService;
    private readonly ILogger<CirclePayoutJob> _logger;

    public CirclePayoutJob(
        ApplicationDbContext context,
        ISavingsCircleService savingsCircleService,
        INotificationQueue notificationQueue,
        IIdentityService identityService,
        ILogger<CirclePayoutJob> logger)
    {
        _context = context;
        _savingsCircleService = savingsCircleService;
        _notificationQueue = notificationQueue;
        _identityService = identityService;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 3)]
    public async Task ProcessDuePayoutsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        
        var dueCircles = await _context.SavingsCircles
            .Include(c => c.Members)
            .Where(c => c.Status == SavingsCircleStatus.Active 
                    // && c.NextCollectionDate != null 
                    //  && c.NextCollectionDate <= now
                     )
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!dueCircles.Any()) return;

        foreach (var circle in dueCircles)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var currentRound = await _context.CircleRounds
                    .Include(r => r.Contributions)
                    .FirstOrDefaultAsync(r => r.SavingsCircleId == circle.Id && 
                                             r.Status == CircleRoundStatus.Collecting, 
                                             cancellationToken);

                if (currentRound == null) continue;

                var expectedTotal = circle.ContributionAmount.ToDecimal() * circle.MemberCount;
                var gracePeriodEnd = currentRound.CollectionDate.AddHours(circle.GracePeriodHours);
                var contributionAmount = circle.ContributionAmount.ToDecimal();

                var activeMembers = circle.Members.Where(m => m.Status == CircleMemberStatus.Active).ToList();
                
                foreach (var member in activeMembers)
                {
                    var contrib = currentRound.Contributions.FirstOrDefault(c => c.CircleMemberId == member.Id);

                    if (contrib == null)
                    {
                        bool paid = false;
                        string paymentRef = "";

                        if (currentRound.RoundNumber == 1)
                        {
                            if (member.DepositHeld.ToDecimal() >= contributionAmount)
                            {
                                member.DepositHeld = Money.FromNaira(member.DepositHeld.ToDecimal() - contributionAmount);
                                paid = true;
                                paymentRef = $"circle-start-{circle.Id}-{member.UserPublicId}";
                            }
                        }
                        else
                        {
                            paid = await _identityService.DebitBalanceAsync(member.UserPublicId!, contributionAmount, cancellationToken);
                            paymentRef = $"circle-contrib-{circle.Id}-{currentRound.RoundNumber}-{member.Id}";
                        }

                        if (paid)
                        {
                            contrib = new Contribution
                            {
                                CircleRoundId = currentRound.Id,
                                CircleMemberId = member.Id,
                                Amount = circle.ContributionAmount,
                                PenaltyApplied = Money.FromNaira(0),
                                Status = ContributionStatus.Collected,
                                CollectedAt = now,
                                PaymentReference = paymentRef
                            };
                            _context.Contributions.Add(contrib);
                            currentRound.PooledAmount = Money.FromNaira(currentRound.PooledAmount.ToDecimal() + contributionAmount);
                            
                            if (currentRound.RoundNumber > 1)
                            {
                                var transaction = new Transaction
                                {
                                    UserPublicId = member.UserPublicId!,
                                    WalletId = null,
                                    Title = $"Circle Contribution: {circle.Name} (Round {currentRound.RoundNumber})",
                                    Amount = Money.FromNaira(contributionAmount),
                                    Type = TransactionType.Debit,
                                    Status = TransactionStatus.Completed,
                                    Reference = paymentRef,
                                    CompletedAt = now
                                };
                                _context.Transactions.Add(transaction);
                            }
                        }
                        else
                        {
                            contrib = new Contribution
                            {
                                CircleRoundId = currentRound.Id,
                                CircleMemberId = member.Id,
                                Amount = circle.ContributionAmount,
                                PenaltyApplied = Money.FromNaira(0),
                                Status = ContributionStatus.Pending
                            };
                            _context.Contributions.Add(contrib);
                        }
                    }
                    else if (contrib.Status == ContributionStatus.Pending)
                    {
                        if (now <= gracePeriodEnd)
                        {
                            bool retryPaid = false;
                            if (currentRound.RoundNumber == 1)
                            {
                                if (member.DepositHeld.ToDecimal() >= contributionAmount)
                                {
                                    member.DepositHeld = Money.FromNaira(member.DepositHeld.ToDecimal() - contributionAmount);
                                    retryPaid = true;
                                }
                            }
                            else
                            {
                                retryPaid = await _identityService.DebitBalanceAsync(member.UserPublicId!, contributionAmount, cancellationToken);
                            }

                            if (retryPaid)
                            {
                                contrib.Status = ContributionStatus.Collected;
                                contrib.CollectedAt = now;
                                contrib.PaymentReference = $"circle-retry-{Guid.NewGuid():N}";
                                currentRound.PooledAmount = Money.FromNaira(currentRound.PooledAmount.ToDecimal() + contributionAmount);
                                
                                if (currentRound.RoundNumber > 1)
                                {
                                    _context.Transactions.Add(new Transaction
                                    {
                                        UserPublicId = member.UserPublicId!,
                                        WalletId = null,
                                        Title = $"Circle Contribution: {circle.Name} (Round {currentRound.RoundNumber})",
                                        Amount = Money.FromNaira(contributionAmount),
                                        Type = TransactionType.Debit,
                                        Status = TransactionStatus.Completed,
                                        Reference = contrib.PaymentReference,
                                        CompletedAt = now
                                    });
                                }
                            }
                        }
                        else
                        {
                            // Grace period expired. Check if the DepositHeld can cover the FULL contribution.
                            if (member.DepositHeld.ToDecimal() >= contributionAmount)
                            {
                                // Full deduction from deposit
                                member.DepositHeld = Money.FromNaira(member.DepositHeld.ToDecimal() - contributionAmount);
                                contrib.Status = ContributionStatus.Collected;
                                contrib.CollectedAt = now;
                                contrib.PaymentReference = $"circle-deposit-fallback-{Guid.NewGuid():N}";
                                currentRound.PooledAmount = Money.FromNaira(currentRound.PooledAmount.ToDecimal() + contributionAmount);

                                _context.Transactions.Add(new Transaction
                                {
                                    UserPublicId = member.UserPublicId!,
                                    WalletId = null,
                                    Title = $"Circle Contribution (From Deposit): {circle.Name} (Round {currentRound.RoundNumber})",
                                    Amount = Money.FromNaira(contributionAmount),
                                    Type = TransactionType.Debit,
                                    Status = TransactionStatus.Completed,
                                    Reference = contrib.PaymentReference,
                                    CompletedAt = now
                                });
                            }
                            else
                            {
                                // Deposit is not up to the contribution. Do not deduct partially. Mark as default.
                                contrib.Status = ContributionStatus.Failed;
                                contrib.FailureReason = "Grace period elapsed without payment, and deposit insufficient to cover contribution.";
                                member.Status = CircleMemberStatus.Defaulted;
                                member.MissedContributions += 1;
                            }
                        }
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);

                var currentPooled = currentRound.PooledAmount.ToDecimal();

                if (currentPooled < expectedTotal)
                {
                    _logger.LogInformation("Circle {CircleId} has incomplete funds. Pooled: {Pooled}, Expected: {Expected}", circle.Id, currentPooled, expectedTotal);
                    
                    var defaulters = circle.Members.Where(m => m.Status == CircleMemberStatus.Defaulted).ToList();
                    if (defaulters.Any())
                    {
                        var organizer = circle.Members.FirstOrDefault(m => m.UserPublicId == circle.OrganizerUserPublicId);
                        if (organizer != null && !string.IsNullOrWhiteSpace(organizer.Identifier))
                        {
                            var defaulterList = string.Join(", ", defaulters.Select(m => m.DisplayName));
                            var message = $"# Action required: Defaulters in '{circle.Name}'\n\n" +
                                $"**The payout for Round {currentRound.RoundNumber} is paused because funds are incomplete.**\n\n" +
                                $"- The following members exceeded the {circle.GracePeriodHours}-hour grace period and are now marked as **defaulted**: {defaulterList}.\n" +
                                "- Please review the circle and take the necessary action.";
                                 
                            _notificationQueue.QueueNotificationEmail(
                                organizer.DisplayName ?? "Organizer",
                                organizer.Identifier,
                                message,
                                $"Action Required: Defaulters in '{circle.Name}'"
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
                    
                    await tx.CommitAsync(cancellationToken);
                    continue;
                }

                var recipient = circle.Members.FirstOrDefault(m => m.UserPublicId == currentRound.RecipientUserPublicId);
                if (recipient == null) throw new Exception("Recipient not found.");

                var recipientWallet = await _context.Wallets
                    .FirstOrDefaultAsync(w => w.UserPublicId == recipient.UserPublicId 
                                           && w.SavingsCircleId == circle.Id 
                                           && w.Type == WalletType.Circle, cancellationToken);

                if (recipientWallet == null) throw new Exception("Recipient circle wallet not found.");
                if (recipientWallet.Status == WalletStatus.Broken || recipientWallet.Status == WalletStatus.Closed)
                    throw new Exception($"Cannot process payout. The circle wallet is {recipientWallet.Status}.");

                recipientWallet.AvailableAmount = Money.FromNaira(recipientWallet.AvailableAmount.ToDecimal() + currentPooled);
                recipientWallet.TotalReleasedAmount = Money.FromNaira(recipientWallet.TotalReleasedAmount.ToDecimal() + currentPooled);
                
                _context.Wallets.Update(recipientWallet);

                var payoutTransaction = new Transaction
                {
                    UserPublicId = recipient.UserPublicId!,
                    WalletId = recipientWallet.Id,
                    Title = $"Circle Payout: {circle.Name} (Round {currentRound.RoundNumber})",
                    Provider = null,
                    Amount = Money.FromNaira(currentPooled),
                    Type = TransactionType.Credit,
                    Status = TransactionStatus.Completed,
                    Reference = $"circle-payout-{circle.Id}-{currentRound.RoundNumber}-{Guid.NewGuid():N}",
                    CompletedAt = now
                };
                
                _context.Transactions.Add(payoutTransaction);
                await _context.SaveChangesAsync(cancellationToken);

                _context.LedgerEntries.Add(new LedgerEntry
                {
                    WalletId = recipientWallet.Id,
                    TransactionId = payoutTransaction.Id,
                    Amount = Money.FromNaira(currentPooled),
                    IsCredit = true
                });

                recipient.TotalReceived = Money.FromNaira(recipient.TotalReceived.ToDecimal() + currentPooled);
                recipient.PayoutReceivedAt = now;

                currentRound.Status = CircleRoundStatus.PaidOut;
                currentRound.PaidOutAmount = currentRound.PooledAmount;
                currentRound.PaidOutAt = now;

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
                    var nextRoundNumber = currentRound.RoundNumber + 1;
                    
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
                        Status = CircleRoundStatus.Collecting
                    };
                    
                    _context.CircleRounds.Add(nextRound);
                    circle.NextCollectionDate = nextDate;
                    circle.NextPayoutDate = nextDate;
                }

                await _context.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                await SendPayoutNotificationsAsync(circle, currentRound, recipient, isCompleted, cancellationToken);

                _logger.LogInformation("Successfully processed payout for circle {CircleId}, round {RoundNumber}.", circle.Id, currentRound.RoundNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process circle {CircleId}.", circle.Id);
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
                _notificationQueue.InAppNotificationAsync(
                    member.UserPublicId!,
                    NotificationType.Wallet,
                    "You received your payout! 🎉",
                    $"Congratulations! You received your payout of ₦{payoutAmount:N0} for Round {paidRound.RoundNumber} in '{circle.Name}'.",
                    detailsUrl,
                    null,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(member.Identifier))
                {
                    _notificationQueue.QueueNotificationEmail(
                        member.DisplayName ?? "Member",
                        member.Identifier,
                        $"# Your payout has arrived\n\n" +
                        $"**You received ₦{payoutAmount:N0} from your '{circle.Name}' circle.**\n\n" +
                        $"- Round: **{paidRound.RoundNumber}**\n" +
                        "- The payout is now available in your circle wallet.",
                        "Your Payout has arrived!");
                }
            }
            else
            {
                var message = isCompleted 
                    ? $"# Update from {circle.Name}\n\n**Round {paidRound.RoundNumber} was paid to {recipientName}.**\n\n- The circle is now complete.\n- You can view the latest round details in the app."
                    : $"# Update from {circle.Name}\n\n**Round {paidRound.RoundNumber} was paid to {recipientName}.**\n\n- Get ready for the next round.\n- You can view the latest circle details in the app.";

                _notificationQueue.InAppNotificationAsync(
                    member.UserPublicId!,
                    NotificationType.Wallet,
                    $"Round {paidRound.RoundNumber} Payout Complete",
                    isCompleted
                        ? $"Round {paidRound.RoundNumber} of '{circle.Name}' was paid to {recipientName}. The circle is now complete!"
                        : $"Round {paidRound.RoundNumber} of '{circle.Name}' was paid to {recipientName}. Get ready for the next round!",
                    detailsUrl,
                    null,
                    cancellationToken);

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