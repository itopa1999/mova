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

        // ─── Nominal refill amount (the policy's intent) ─────
        var nominalRefill = policy.RefillAmountType == RefillAmountType.Fixed
            ? wallet.TargetAmount.ToDecimal()
            : policy.RefillAmount.ToDecimal();

        if (nominalRefill <= 0)
        {
            op.Fail("Refill amount resolved to zero or less.");
            await LogFailedAsync(
                wallet, policy, firedBy,
                Money.FromNaira(0),
                "Refill amount is invalid.", cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = "Invalid refill amount." };
        }

        // ─── Fetch user's main balance ───────────────────────
        var user = await _identityService.GetByIdentifierAsync(
            wallet.UserPublicId, cancellationToken);

        if (user is null)
        {
            op.Fail("User not found.");
            await LogFailedAsync(
                wallet, policy, firedBy,
                Money.FromNaira(nominalRefill),
                "User account not found.", cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = "User not found." };
        }

        var mainBalance = user.Balance.ToDecimal();

        if (mainBalance <= 0)
        {
            var reason = "Main account balance is empty.";
            op.Success($"Skipped — {reason}");
            await LogSkippedAsync(wallet, policy, firedBy, reason, cancellationToken);
            await NotifySkippedAsync(wallet, nominalRefill, reason, cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = reason };
        }

        // ─── Wallet release amount ───────────────────────────
        var releaseAmount = wallet.Rule?.Amount.ToDecimal() ?? 0m;
        if (releaseAmount <= 0)
        {
            op.Fail("Wallet rule has invalid release amount.");
            await LogFailedAsync(
                wallet, policy, firedBy,
                Money.FromNaira(nominalRefill),
                "Wallet release amount is invalid.", cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = "Invalid release amount." };
        }

        var payoutDestinationString = wallet.PayoutDestination switch
        {
            PayoutDestination.Bank => "bank",
            PayoutDestination.Wallet => "wallet",
            PayoutDestination.Main => "main",
            _ => "bank",
        };

        // ─────────────────────────────────────────────────────
        // Decide the actual principal that will land in the wallet.
        // Fee is ALWAYS charged on this — never the target, never
        // the existing locked amount. Only the delta we add.
        // ─────────────────────────────────────────────────────

        decimal refillAmount;
        var isPartialRefill = false;

        if (policy.RefillUntilMainBalanceExhausted)
        {
            refillAmount = Math.Min(nominalRefill, mainBalance);
            isPartialRefill = refillAmount < nominalRefill;

            if (isPartialRefill)
            {
                op.Success(
                    $"Partial refill — main balance ₦{mainBalance:N0} is below " +
                    $"nominal refill ₦{nominalRefill:N0}. Using ₦{refillAmount:N0}.");
            }
        }
        else
        {
            refillAmount = nominalRefill;

            // Preview fee against the refill amount only.
            var releasesPreview = (int)Math.Ceiling(refillAmount / releaseAmount);
            var feesPreview = WalletFeeHelper.Calculate(
                targetAmount: refillAmount,
                releaseAmount: releaseAmount,
                payoutDestination: payoutDestinationString,
                releases: releasesPreview);

            var totalDebitPreview = refillAmount + feesPreview.TotalMovaCharges;
            var mainAfterDebit = mainBalance - totalDebitPreview;
            var minMain = policy.MinMainBalance.ToDecimal();

            if (mainAfterDebit < minMain)
            {
                var reason =
                    $"Main account would drop below your ₦{minMain:N0} floor " +
                    $"(needed ₦{totalDebitPreview:N0}, available ₦{mainBalance:N0}).";

                op.Success($"Skipped — {reason}");
                await LogSkippedAsync(wallet, policy, firedBy, reason, cancellationToken);
                await NotifySkippedAsync(wallet, refillAmount, reason, cancellationToken);
                return new RenewalOutcome { Skipped = true, SkipReason = reason };
            }
        }

        if (refillAmount <= 0)
        {
            var reason = "Resolved refill amount is zero.";
            op.Success($"Skipped — {reason}");
            await LogSkippedAsync(wallet, policy, firedBy, reason, cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = reason };
        }

        // ─────────────────────────────────────────────────────
        // Compute the fee ONCE, against the final refillAmount.
        // If the fee won't fit inside the balance in partial mode,
        // shrink the refill until it does, and recompute the fee
        // against the new (smaller) refill. This is the only
        // place fees are ever calculated, and it's always against
        // the amount that lands in LockedAmount — nothing else.
        // ─────────────────────────────────────────────────────

        var releases = (int)Math.Ceiling(refillAmount / releaseAmount);

        if (releases <= 0)
        {
            var reason = "Refill too small to cover one release.";
            op.Success($"Skipped — {reason}");
            await LogSkippedAsync(wallet, policy, firedBy, reason, cancellationToken);
            return new RenewalOutcome { Skipped = true, SkipReason = reason };
        }

        var fees = WalletFeeHelper.Calculate(
            targetAmount: refillAmount,
            releaseAmount: releaseAmount,
            payoutDestination: payoutDestinationString,
            releases: releases);

        var totalDebit = refillAmount + fees.TotalMovaCharges;

        if (policy.RefillUntilMainBalanceExhausted && totalDebit > mainBalance)
        {
            var availableForRefill = mainBalance - fees.TotalMovaCharges;

            if (availableForRefill <= 0)
            {
                var reason =
                    $"Main balance ₦{mainBalance:N0} can't cover the " +
                    $"₦{fees.TotalMovaCharges:N0} fee.";

                op.Success($"Skipped — {reason}");
                await LogSkippedAsync(wallet, policy, firedBy, reason, cancellationToken);
                await NotifySkippedAsync(wallet, refillAmount, reason, cancellationToken);
                return new RenewalOutcome { Skipped = true, SkipReason = reason };
            }

            refillAmount = availableForRefill;
            releases = (int)Math.Ceiling(refillAmount / releaseAmount);

            if (releases <= 0)
            {
                var reason = "Refill too small to cover one release after fee.";
                op.Success($"Skipped — {reason}");
                await LogSkippedAsync(wallet, policy, firedBy, reason, cancellationToken);
                return new RenewalOutcome { Skipped = true, SkipReason = reason };
            }

            // Recompute fee against the final refillAmount.
            fees = WalletFeeHelper.Calculate(
                targetAmount: refillAmount,
                releaseAmount: releaseAmount,
                payoutDestination: payoutDestinationString,
                releases: releases);

            totalDebit = refillAmount + fees.TotalMovaCharges;
            isPartialRefill = true;

            op.Success(
                $"Partial refill adjusted for fee — refilling ₦{refillAmount:N0} " +
                $"+ fee ₦{fees.TotalMovaCharges:N0} (total debit ₦{totalDebit:N0}).");
        }

        // ─── Execute the renewal ─────────────────────────────
        op.Success(
            $"Refilling ₦{refillAmount:N0} + fee ₦{fees.TotalMovaCharges:N0}. " +
            $"Trigger: {firedBy}. Partial: {isPartialRefill}");

        long? renewalEventId = null;
        string? failureReason = null;

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
            // This is the NEW locked amount delta, and it's exactly
            // what the fee above was computed against.
            var refillMoney = Money.FromNaira(refillAmount);

            wallet.LockedAmount += refillMoney;
            wallet.FundedAmount += refillMoney;

            if (wallet.Status == WalletStatus.Completed)
            {
                wallet.Status = WalletStatus.Active;
                wallet.CompletedAt = null;
            }

            // ─── Refill transaction ──────────────────────────
            var refillTitle = isPartialRefill
                ? "Wallet Refilled (Partial)"
                : "Wallet Refilled";

            var refillTx = new Transaction
            {
                UserPublicId = wallet.UserPublicId,
                WalletId = wallet.Id,
                Title = refillTitle,
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
            // fees.TotalMovaCharges was computed against the FINAL
            // refillAmount (the new locked amount delta), not the
            // target and not the pre-existing locked balance.
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

            var eventReason = isPartialRefill
                ? $"Partial refill — main balance was below the nominal amount. " +
                  $"Refilled ₦{refillAmount:N0}."
                : null;

            var renewalEvent = new RenewalEvent
            {
                WalletId = wallet.Id,
                RenewalPolicyId = policy.Id,
                UserPublicId = wallet.UserPublicId,
                OccurredAt = DateTimeOffset.UtcNow,
                Result = RenewalResult.Succeeded,
                Reason = eventReason,
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
                $"Refilled: ₦{refillAmount:N0}, Partial: {isPartialRefill}");

            try
            {
                await NotifySucceededAsync(
                    wallet, refillAmount, firedBy, isPartialRefill, cancellationToken);
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
        bool isPartialRefill,
        CancellationToken cancellationToken)
    {
        var trigger = firedBy == RenewalTriggerType.OnThreshold
            ? "your wallet's balance dropped low"
            : "your wallet completed a cycle";

        var title = $"{wallet.Name} wallet refilled";

        var message = isPartialRefill
            ? $"₦{amount:N0} has been added to your {wallet.Name} wallet " +
              $"(a partial refill — your main balance was below the usual " +
              $"refill amount) because {trigger}. The schedule continues as before."
            : $"₦{amount:N0} has been added to your {wallet.Name} wallet " +
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