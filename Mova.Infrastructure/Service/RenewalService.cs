using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.Shared;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Services;

public sealed class RenewalService : IRenewalService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RenewalService> _logger;
    private readonly IIdentityService _identityService;
    private readonly IWalletRuleService _walletRuleService;
    private readonly INotificationQueue _notificationQueue;

    public RenewalService(
        ApplicationDbContext context,
        ILogger<RenewalService> logger,
        IIdentityService identityService,
        IWalletRuleService walletRuleService,
        INotificationQueue notificationQueue)
    {
        _context = context;
        _logger = logger;
        _identityService = identityService;
        _walletRuleService = walletRuleService;
        _notificationQueue = notificationQueue;
    }

    public async Task<RenewalOutcome> TryRenewWalletAsync(
        long walletId,
        RenewalTriggerType firedBy,
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(
            _logger,
            "TryRenewWallet",
            ("WalletId", walletId),
            ("FiredBy", firedBy));

        var wallet = await _context.Wallets
            .Include(w => w.Rule)
            .FirstOrDefaultAsync(w => w.Id == walletId, cancellationToken);

        if (wallet is null)
        {
            op.Fail("Wallet not found.");
            return new RenewalOutcome { Skipped = true, SkipReason = "Wallet not found." };
        }

        var policy = await _context.Set<RenewalPolicy>()
            .FirstOrDefaultAsync(p => p.WalletId == walletId, cancellationToken);

        if (policy is null)
        {
            op.Success("No renewal policy — nothing to do.");
            return new RenewalOutcome { Skipped = true, SkipReason = "No policy." };
        }

        if (!policy.IsEnabled)
        {
            op.Success("Policy disabled.");
            await LogSkippedAsync(
                wallet, policy, firedBy,
                "Automation is disabled.", cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = "Automation disabled." };
        }

        if (policy.Status != RenewalStatus.Active)
        {
            op.Success($"Policy status is {policy.Status}.");
            await LogSkippedAsync(
                wallet, policy, firedBy,
                $"Automation is {policy.Status}.", cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = $"Status is {policy.Status}." };
        }

        if (policy.TriggerType != firedBy)
        {
            op.Success($"Trigger mismatch — policy is {policy.TriggerType}, fired by {firedBy}.");
            return new RenewalOutcome { Skipped = true, SkipReason = "Trigger mismatch." };
        }

        // ─── Determine refill amount ─────────────────────────
        var refillAmount = policy.RefillAmountType == RefillAmountType.Fixed
            ? wallet.TargetAmount.ToDecimal()
            : policy.RefillAmount.ToDecimal();

        if (refillAmount <= 0)
        {
            op.Fail("Refill amount resolved to zero or less.");
            await LogFailedAsync(
                wallet, policy, firedBy,
                Money.FromNaira(0),
                "Refill amount is invalid.", cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = "Invalid refill amount." };
        }

        // ─── Guardrail: MaxRenewals ──────────────────────────
        if (policy.MaxRenewals.HasValue
            && policy.RenewalsCount >= policy.MaxRenewals.Value)
        {
            op.Success("Max renewals reached.");
            await LogSkippedAsync(
                wallet, policy, firedBy,
                $"Maximum renewals ({policy.MaxRenewals}) reached.", cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = "Max renewals reached." };
        }

        // ─── Compute the fee for this cycle ──────────────────
        var releaseAmount = wallet.Rule?.Amount.ToDecimal() ?? 0m;
        if (releaseAmount <= 0)
        {
            op.Fail("Wallet rule has invalid release amount.");
            await LogFailedAsync(
                wallet, policy, firedBy,
                Money.FromNaira(refillAmount),
                "Wallet release amount is invalid.", cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = "Invalid release amount." };
        }

        var releases = (int)Math.Ceiling(refillAmount / releaseAmount);

        var payoutDestinationString = wallet.PayoutDestination switch
        {
            PayoutDestination.Bank => "bank",
            PayoutDestination.Wallet => "wallet",
            PayoutDestination.Main => "main",
            _ => "bank",
        };

        var fees = WalletFeeHelper.Calculate(
            targetAmount: refillAmount,
            releaseAmount: releaseAmount,
            payoutDestination: payoutDestinationString,
            releases: releases);

        var totalDebit = refillAmount + fees.TotalMovaCharges;

        // ─── Guardrail: MinMainBalance ───────────────────────
        var user = await _identityService.GetByIdentifierAsync(
            wallet.UserPublicId, cancellationToken);

        if (user is null)
        {
            op.Fail("User not found.");
            await LogFailedAsync(
                wallet, policy, firedBy,
                Money.FromNaira(refillAmount),
                "User account not found.", cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = "User not found." };
        }

        var mainBalance = user.Balance.ToDecimal();
        var mainAfterDebit = mainBalance - totalDebit;
        var minMain = policy.MinMainBalance.ToDecimal();

        if (mainAfterDebit < minMain)
        {
            var reason =
                $"Main account would drop below your ₦{minMain:N0} floor " +
                $"(needed ₦{totalDebit:N0}, available ₦{mainBalance:N0}).";

            op.Success($"Skipped — {reason}");

            await LogSkippedAsync(
                wallet, policy, firedBy, reason, cancellationToken);

            await NotifySkippedAsync(
                wallet, refillAmount, reason, cancellationToken);

            return new RenewalOutcome { Skipped = true, SkipReason = reason };
        }

        // ─── Execute the renewal ─────────────────────────────
        op.Success(
            $"Refilling ₦{refillAmount:N0} + fee ₦{fees.TotalMovaCharges:N0}. " +
            $"Trigger: {firedBy}");

        long? renewalEventId = null;
        decimal refilledAmount = refillAmount;
        string? failureReason = null;

        // If the caller already opened a transaction (e.g. the release job),
        // participate in theirs. Otherwise, open our own.
        var ownsTransaction = _context.Database.CurrentTransaction is null;

        if (ownsTransaction)
        {
            await _context.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var debited = await _identityService.DebitBalanceAsync(
                wallet.UserPublicId, totalDebit, cancellationToken);

            if (!debited)
            {
                if (ownsTransaction)
                {
                    await _context.Database.RollbackTransactionAsync(cancellationToken);
                }

                failureReason = "Insufficient main account balance.";
                op.Fail(failureReason);

                // NOTE: LogFailedAsync writes a RenewalEvent. When the outer
                // transaction is aborted, this write may not persist. The
                // primary failure record is the `op.Fail` above.
                try
                {
                    await LogFailedAsync(
                        wallet, policy, firedBy,
                        Money.FromNaira(refillAmount),
                        failureReason, cancellationToken);
                }
                catch (Exception logEx)
                {
                    _logger.LogError(
                        logEx,
                        "Failed to log renewal failure for wallet {WalletId}.",
                        wallet.Id);
                }

                await NotifySkippedAsync(
                    wallet, refillAmount, failureReason, cancellationToken);

                return new RenewalOutcome { Skipped = true, SkipReason = failureReason };
            }

            // ─── Refill wallet: add on top of existing locked ──
            var refillMoney = Money.FromNaira(refillAmount);

            wallet.LockedAmount += refillMoney;
            wallet.FundedAmount += refillMoney;

            // Re-activate if wallet had completed
            if (wallet.Status == WalletStatus.Completed)
            {
                wallet.Status = WalletStatus.Active;
                wallet.CompletedAt = null;
            }

            // ─── Refill transaction ──────────────────────────
            var refillTx = new Transaction
            {
                UserPublicId = wallet.UserPublicId,
                WalletId = wallet.Id,
                Title = "Wallet Refilled",
                Amount = refillMoney,
                Type = TransactionType.Refill,
                Status = TransactionStatus.Completed,
                Reference = $"renewal-refill:{wallet.Id}:{Guid.NewGuid():N}",
                CompletedAt = DateTimeOffset.UtcNow,
            };

            await _context.Transactions.AddAsync(refillTx, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            var refillLedger = new LedgerEntry
            {
                WalletId = wallet.Id,
                TransactionId = refillTx.Id,
                Amount = refillMoney,
                IsCredit = true,
            };

            await _context.LedgerEntries.AddAsync(refillLedger, cancellationToken);

            // ─── Fee transaction ─────────────────────────────
            var feeTx = new Transaction
            {
                UserPublicId = wallet.UserPublicId,
                WalletId = wallet.Id,
                Title = "Automation Refill Fee",
                Amount = Money.FromNaira(fees.TotalMovaCharges),
                Type = TransactionType.Fee,
                Status = TransactionStatus.Completed,
                Reference = $"renewal-fee:{wallet.Id}:{Guid.NewGuid():N}",
                CompletedAt = DateTimeOffset.UtcNow,
            };

            await _context.Transactions.AddAsync(feeTx, cancellationToken);

            // ─── Schedule first release of the new cycle ─────
            // Only for OnCompletion. For OnThreshold, the wallet keeps
            // its existing cadence — we just added money on top, so the
            // normal next release will happen on schedule. Scheduling
            // here would double-fire.
            if (firedBy == RenewalTriggerType.OnCompletion)
            {
                var rule = wallet.Rule;
                if (rule is not null)
                {
                    var anchor = DateTimeOffset.UtcNow.AddSeconds(-1);

                    var nextRelease = await _walletRuleService.GetNextReleaseAsync(
                        rule, anchor, cancellationToken);

                    if (nextRelease is not null)
                    {
                        var firstAmount =
                            nextRelease.Amount.MinorUnits > wallet.LockedAmount.MinorUnits
                                ? wallet.LockedAmount
                                : nextRelease.Amount;

                        var newRelease = new ScheduledRelease
                        {
                            WalletId = wallet.Id,
                            WalletRuleId = rule.Id,
                            Amount = firstAmount,
                            ScheduledFor = nextRelease.ScheduledFor,
                            Status = ReleaseStatus.Scheduled,
                            ReleasedAt = null
                        };

                        await _context.ScheduledReleases.AddAsync(
                            newRelease, cancellationToken);
                    }
                }
            }

            // ─── Increment counter + write event ─────────────
            policy.RenewalsCount += 1;

            var renewalEvent = new RenewalEvent
            {
                WalletId = wallet.Id,
                RenewalPolicyId = policy.Id,
                UserPublicId = wallet.UserPublicId,
                OccurredAt = DateTimeOffset.UtcNow,
                Result = RenewalResult.Succeeded,
                Reason = null,
                Amount = refillMoney,
                TransactionId = refillTx.Id,
            };

            await _context.Set<RenewalEvent>().AddAsync(
                renewalEvent, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            if (ownsTransaction)
            {
                await _context.Database.CommitTransactionAsync(cancellationToken);
            }

            renewalEventId = renewalEvent.Id;

            op.Success(
                $"Renewal succeeded. EventId: {renewalEventId}, " +
                $"Refilled: ₦{refillAmount:N0}");

            // ─── Post-commit notification ────────────────────
            try
            {
                await NotifySucceededAsync(
                    wallet, refillAmount, firedBy, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Renewal notification failed for wallet {WalletId}.",
                    wallet.Id);
            }

            return new RenewalOutcome
            {
                Executed = true,
                RenewalEventId = renewalEventId,
                RefilledAmount = refillMoney,
            };
        }
        catch (Exception ex)
        {
            if (ownsTransaction)
            {
                await _context.Database.RollbackTransactionAsync(cancellationToken);
            }

            op.Fail(
                $"Error during renewal for wallet {wallet.Id}.",
                ex);

            // Best-effort failure log
            try
            {
                await LogFailedAsync(
                    wallet, policy, firedBy,
                    Money.FromNaira(refillAmount),
                    "An error occurred during renewal.", cancellationToken);
            }
            catch (Exception logEx)
            {
                _logger.LogError(
                    logEx,
                    "Failed to log renewal failure for wallet {WalletId}.",
                    wallet.Id);
            }

            return new RenewalOutcome
            {
                Skipped = true,
                SkipReason = "Error during renewal.",
            };
        }
    }

    // ─── Event log helpers ────────────────────────────────

    private async Task LogSkippedAsync(
        Wallet wallet,
        RenewalPolicy policy,
        RenewalTriggerType firedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        var ev = new RenewalEvent
        {
            WalletId = wallet.Id,
            RenewalPolicyId = policy.Id,
            UserPublicId = wallet.UserPublicId,
            OccurredAt = DateTimeOffset.UtcNow,
            Result = RenewalResult.Skipped,
            Reason = reason,
            Amount = policy.RefillAmountType == RefillAmountType.Fixed
                ? wallet.TargetAmount
                : policy.RefillAmount,
            TransactionId = null,
        };

        await _context.Set<RenewalEvent>().AddAsync(ev, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task LogFailedAsync(
        Wallet wallet,
        RenewalPolicy policy,
        RenewalTriggerType firedBy,
        Money amount,
        string reason,
        CancellationToken cancellationToken)
    {
        var ev = new RenewalEvent
        {
            WalletId = wallet.Id,
            RenewalPolicyId = policy.Id,
            UserPublicId = wallet.UserPublicId,
            OccurredAt = DateTimeOffset.UtcNow,
            Result = RenewalResult.Failed,
            Reason = reason,
            Amount = amount,
            TransactionId = null,
        };

        await _context.Set<RenewalEvent>().AddAsync(ev, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ─── Notification helpers ─────────────────────────────

    private Task NotifySucceededAsync(
        Wallet wallet,
        decimal amount,
        RenewalTriggerType firedBy,
        CancellationToken cancellationToken)
    {
        var trigger = firedBy == RenewalTriggerType.OnThreshold
            ? "your wallet's balance dropped low"
            : "your wallet completed a cycle";

        var title = $"{wallet.Name} wallet refilled";
        var message =
            $"₦{amount:N0} has been added to your {wallet.Name} wallet " +
            $"because {trigger}. The schedule continues as before.";

        _notificationQueue.InAppNotificationAsync(
            wallet.UserPublicId,
            NotificationType.Wallet,
            title,
            message,
            $"/wallet/{wallet.Id}",
            null,
            CancellationToken.None);

        return Task.CompletedTask;
    }

    private Task NotifySkippedAsync(
        Wallet wallet,
        decimal amount,
        string reason,
        CancellationToken cancellationToken)
    {
        var title = $"{wallet.Name} refill skipped";
        var message =
            $"We couldn't refill your {wallet.Name} wallet. {reason}";

        _notificationQueue.InAppNotificationAsync(
            wallet.UserPublicId,
            NotificationType.Wallet,
            title,
            message,
            $"/wallet/{wallet.Id}",
            null,
            CancellationToken.None);

        return Task.CompletedTask;
    }
}