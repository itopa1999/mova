using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class ProcessPayoutsJob
{
    private const int MaxPayoutsPerRun = 100;
    private const int MaxTransferAttempts = 3;
    private const int PerPayoutTimeoutSeconds = 90;

    private static readonly TimeSpan NotFoundGracePeriod = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxProcessingDuration = TimeSpan.FromHours(24);

    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessPayoutsJob> _logger;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly IPaystackService _paystackService;
    private readonly IMonnifyService _monnifyService;
    private readonly IFlutterwaveService _flutterwaveService;
    private readonly IIdentityService _identityService;

    public ProcessPayoutsJob(
        ApplicationDbContext context,
        ILogger<ProcessPayoutsJob> logger,
        IFeatureFlagService featureFlagService,
        IPaystackService paystackService,
        IMonnifyService monnifyService,
        IFlutterwaveService flutterwaveService,
        IIdentityService identityService)
    {
        _context = context;
        _logger = logger;
        _featureFlagService = featureFlagService;
        _paystackService = paystackService;
        _monnifyService = monnifyService;
        _flutterwaveService = flutterwaveService;
        _identityService = identityService;
    }

    [DisableConcurrentExecution(1800)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "ProcessPayouts");

        if (!await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.AllowWithdrawFunds, cancellationToken))
        {
            op.Success("Withdrawals are disabled. Skipping payout processing.");
            return;
        }

        var payoutIds = await _context.Payouts
            .AsNoTracking()
            .Where(x =>
                x.Status == PayoutStatus.Pending ||
                x.Status == PayoutStatus.Processing)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .Take(MaxPayoutsPerRun)
            .ToListAsync(cancellationToken);

        if (payoutIds.Count == 0)
        {
            op.Success("No pending payouts to process.");
            return;
        }

        var processed = 0;

        foreach (var payoutId in payoutIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                op.Success($"Cancelled after {processed} payout(s).");
                return;
            }

            try
            {
                using var perPayoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(PerPayoutTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, perPayoutCts.Token);

                await ProcessPayoutAsync(payoutId, linkedCts.Token);
                processed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                op.Success($"Job cancelled at payout {payoutId}. {processed} processed.");
                return;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Payout {PayoutId} timed out after {Timeout}s.",
                    payoutId, PerPayoutTimeoutSeconds);
            }
            catch (Exception ex)
            {
                op.Error($"Unhandled error processing payout {payoutId}.", ex);
            }
        }

        op.Success($"Processed {processed} payout(s).");
    }

    private async Task ProcessPayoutAsync(long payoutId, CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "ProcessPayout", ("PayoutId", payoutId));

        var payout = await _context.Payouts
            .Include(x => x.Wallet)
            .Include(x => x.BankAccount)
            .FirstOrDefaultAsync(x => x.Id == payoutId, cancellationToken);

        if (payout is null)
            return;

        if (payout.Status is
            PayoutStatus.Successful or
            PayoutStatus.Failed or
            PayoutStatus.Reversed)
        {
            return;
        }

        if (payout.Wallet is null)
        {
            await MarkFailedAsync(payout, "Wallet was not found.", cancellationToken);
            return;
        }

        if (payout.Amount.MinorUnits <= 0)
        {
            await MarkFailedAsync(payout, "Payout amount must be greater than zero.", cancellationToken);
            return;
        }

        switch (payout.Destination)
        {
            case PayoutDestination.Bank:
                await ProcessBankPayoutAsync(payout, op, cancellationToken);
                break;
            case PayoutDestination.Main:
                await ProcessMainPayoutAsync(payout, op, cancellationToken);
                break;
            case PayoutDestination.Utilities:
                await ProcessUtilityPayoutAsync(payout, op, cancellationToken);
                break;
            default:
                await MarkFailedAsync(
                    payout,
                    $"Unsupported payout destination: {payout.Destination}",
                    cancellationToken);
                break;
        }
    }

    // ─── BANK ─────────────────────────────────────────────────

    private async Task ProcessBankPayoutAsync(
        Payout payout, OperationLogger op, CancellationToken cancellationToken)
    {
        if (payout.BankAccount is null)
        {
            await MarkFailedAsync(payout, "Bank account was not found.", cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(payout.Reference))
        {
            await MarkFailedAsync(payout, "Payout reference is missing.", cancellationToken);
            return;
        }

        var gateway = await ResolveGatewayAsync(cancellationToken);
        if (gateway is null)
        {
            await MarkFailedAsync(payout, "No payout gateway is currently enabled.", cancellationToken);
            return;
        }

        // IDEMPOTENCY GUARD: if we ever attempted a send, VERIFY before anything else.
        // This is the single most important protection against double-paying.
        if (payout.InitiatedAt is not null)
        {
            await VerifyExistingTransferAsync(payout, gateway.Value, op, cancellationToken);
            return;
        }

        // First attempt. Record intent BEFORE the network call so a crash
        // between send and record is still recoverable via verify next run.
        payout.Status = PayoutStatus.Processing;
        payout.Provider = gateway.Value.ToString();
        payout.InitiatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);


        var result = await SendTransferAsync(gateway.Value, payout, cancellationToken);
        await ApplyTransferResultAsync(payout, result, op, cancellationToken);
    }

    private async Task VerifyExistingTransferAsync(
        Payout payout, PaymentProvider gateway, OperationLogger op, CancellationToken cancellationToken)
    {
        // Safety net for stuck payouts.
        if (payout.InitiatedAt is not null &&
            DateTimeOffset.UtcNow - payout.InitiatedAt.Value > MaxProcessingDuration)
        {
            await MarkFailedAsync(
                payout,
                $"Payout exceeded maximum processing window ({MaxProcessingDuration.TotalHours}h).",
                cancellationToken);
            return;
        }

        var verify = await VerifyTransferAsync(gateway, payout, cancellationToken);

        // Transient verify failure — do NOT mark failed, retry next run.
        if (!verify.CallSucceeded)
        {
            op.Warning($"Verify failed transiently: {verify.Message}. Retrying next run.");
            return;
        }

        if (!verify.Found)
        {
            var age = DateTimeOffset.UtcNow - (payout.InitiatedAt ?? DateTimeOffset.UtcNow);
            if (age < NotFoundGracePeriod)
            {
                op.Warning($"Not found at provider but only {age.TotalSeconds:F0}s old. Waiting.");
                return;
            }

            // Grace period exceeded. Safe to re-send with the SAME reference.
            // Provider dedupes by reference, so this is a no-op if the first
            // attempt actually landed.
            payout.FailedAttempts++;
            if (payout.FailedAttempts >= MaxTransferAttempts)
            {
                await MarkFailedAsync(
                    payout,
                    $"Transfer not found at provider after {MaxTransferAttempts} attempts.",
                    cancellationToken);
                return;
            }

            op.Warning($"Not found after grace ({payout.FailedAttempts}/{MaxTransferAttempts}). Re-sending.");
            var sendResult = await SendTransferAsync(gateway, payout, cancellationToken);
            await ApplyTransferResultAsync(payout, sendResult, op, cancellationToken);
            return;
        }

        await ApplyTransferResultAsync(payout, verify, op, cancellationToken);
    }

    private async Task ApplyTransferResultAsync(
        Payout payout, TransferResult result, OperationLogger op, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(result.Reference))
        {
            payout.ProviderReference ??= result.Reference;
        }

        // Transient — leave as Processing, next run verifies.
        if (!result.CallSucceeded)
        {
            op.Warning($"Transfer call failed transiently: {result.Message}. Retrying next run.");
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        switch (result.Status)
        {
            case TransactionStatus.Completed:
                await MarkSuccessfulAsync(payout, cancellationToken);
                op.Success("Transfer completed.");
                return;

            case TransactionStatus.Failed:
            case TransactionStatus.Abandoned:
                await MarkFailedAsync(
                    payout,
                    result.Message ?? "Transfer failed at provider.",
                    cancellationToken);
                op.Success("Transfer marked failed (conclusive).");
                return;

            case TransactionStatus.Reversed:
                payout.Status = PayoutStatus.Reversed;
                payout.FailureReason = result.Message ?? "Transfer was reversed at provider.";
                payout.FailedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                op.Success("Transfer marked reversed.");
                return;

            case TransactionStatus.Pending:
            case TransactionStatus.Processing:
                payout.Status = PayoutStatus.Processing;
                await _context.SaveChangesAsync(cancellationToken);
                return;
        }
    }

    // ─── MAIN ─────────────────────────────────────────────────

    private async Task ProcessMainPayoutAsync(
        Payout payout, OperationLogger op, CancellationToken cancellationToken)
    {
        if (payout.MainCreditedAt is not null)
        {
            payout.Provider = "MainBalance";
            payout.ProviderReference ??= $"main-credit:{payout.Id}";
            await MarkSuccessfulAsync(payout, cancellationToken);
            op.Success("Main payout already credited — marked successful.");
            return;
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var credited = await _identityService.CreditBalanceAsync(
                payout.UserPublicId,
                payout.Amount.ToDecimal(),
                cancellationToken);

            if (!credited)
            {
                await tx.RollbackAsync(cancellationToken);
                await MarkFailedAsync(payout, "Failed to credit main MOVA balance.", cancellationToken);
                return;
            }

            payout.MainCreditedAt = DateTimeOffset.UtcNow;
            payout.Provider = "MainBalance";
            payout.ProviderReference = $"main-credit:{payout.Id}";

            if (payout.Wallet is not null)
            {
                payout.Wallet.TotalWithdrawnAmount = Money.FromMinorUnits(
                    checked(payout.Wallet.TotalWithdrawnAmount.MinorUnits + payout.Amount.MinorUnits),
                    payout.Wallet.TotalWithdrawnAmount.Currency);
            }

            if (payout.Status != PayoutStatus.Successful)
            {
                payout.Status = PayoutStatus.Successful;
                payout.CompletedAt = DateTimeOffset.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success("Release credited to main MOVA balance.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            op.Error($"Error processing main payout {payout.Id}.", ex);
            throw;
        }
    }

    // ─── UTILITIES ─────────────────────────────────────────────

    private async Task ProcessUtilityPayoutAsync(
        Payout payout, OperationLogger op, CancellationToken cancellationToken)
    {
        if (payout.Wallet is null)
        {
            await MarkFailedAsync(payout, "Wallet was not found.", cancellationToken);
            return;
        }

        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var reference = $"UTID-{Guid.NewGuid():N}";

            payout.Wallet.TotalWithdrawnAmount = Money.FromMinorUnits(
                checked(payout.Wallet.TotalWithdrawnAmount.MinorUnits + payout.Amount.MinorUnits),
                payout.Wallet.TotalWithdrawnAmount.Currency);

            payout.Provider = "Utility";
            payout.ProviderReference = reference;

            if (payout.Status != PayoutStatus.Successful)
            {
                payout.Status = PayoutStatus.Successful;
                payout.CompletedAt = DateTimeOffset.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success($"Utility payout processed. Reference: {reference}");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            op.Error($"Error processing utility payout {payout.Id}.", ex);
            throw;
        }
    }

    // ─── GATEWAY RESOLUTION ───────────────────────────────────

    private async Task<PaymentProvider?> ResolveGatewayAsync(CancellationToken cancellationToken)
    {
        if (await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.PayoutsViaMonnify, cancellationToken))
            return PaymentProvider.Monnify;

        if (await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.PayoutsViaFlutterwave, cancellationToken))
            return PaymentProvider.Flutterwave;

        if (await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.PayoutsViaPaystack, cancellationToken))
            return PaymentProvider.Paystack;

        return null;
    }

    // ─── TRANSFER ─────────────────────────────────────────────

    private Task<TransferResult> SendTransferAsync(
        PaymentProvider gateway, Payout payout, CancellationToken cancellationToken)
        => gateway switch
        {
            PaymentProvider.Paystack => _paystackService.TransferAsync(
                payout.BankAccount!, payout.Amount, payout.Reference, cancellationToken),

            PaymentProvider.Monnify => _monnifyService.TransferAsync(
                payout.BankAccount!, payout.Amount, payout.Reference, cancellationToken),

            PaymentProvider.Flutterwave => _flutterwaveService.TransferAsync(
                payout.BankAccount!, payout.Amount, payout.Reference, cancellationToken),

            _ => throw new InvalidOperationException($"Unsupported gateway: {gateway}"),
        };

    private Task<TransferResult> VerifyTransferAsync(
        PaymentProvider gateway, Payout payout, CancellationToken cancellationToken)
        => gateway switch
        {
            PaymentProvider.Paystack => _paystackService.VerifyTransferAsync(
                payout.Reference, cancellationToken),

            PaymentProvider.Monnify => _monnifyService.VerifyTransferAsync(
                payout.Reference, cancellationToken),

            PaymentProvider.Flutterwave => _flutterwaveService.VerifyTransferAsync(
                payout.Reference, cancellationToken),

            _ => throw new InvalidOperationException($"Unsupported gateway: {gateway}"),
        };

    // ─── STATUS TRANSITIONS ───────────────────────────────────

    private async Task MarkSuccessfulAsync(Payout payout, CancellationToken cancellationToken)
    {
        if (payout.Status == PayoutStatus.Successful) return;

        payout.Status = PayoutStatus.Successful;
        payout.CompletedAt = DateTimeOffset.UtcNow;
        
        // todo: for now we use here, buh later we use the webhook please. 

        // Increment TotalWithdrawnAmount for Bank payouts here so status and
        // stats are updated atomically. This is the ONLY place Bank payouts
        // should increment. The webhook handler must NOT do it separately.
        if (payout.Destination == PayoutDestination.Bank && payout.Wallet is not null)
        {
            payout.Wallet.TotalWithdrawnAmount = Money.FromMinorUnits(
                checked(payout.Wallet.TotalWithdrawnAmount.MinorUnits + payout.Amount.MinorUnits),
                payout.Wallet.TotalWithdrawnAmount.Currency);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(Payout payout, string reason, CancellationToken cancellationToken)
    {
        payout.Status = PayoutStatus.Failed;
        payout.FailureReason = reason;
        payout.FailedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}