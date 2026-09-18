using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class ProcessPayoutsJob
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessPayoutsJob> _logger;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly IPaystackService _paystackService;
    private readonly IMonnifyService _monnifyService;
    private readonly IFlutterwaveService _flutterwaveService;

    public ProcessPayoutsJob(
        ApplicationDbContext context,
        ILogger<ProcessPayoutsJob> logger,
        IFeatureFlagService featureFlagService,
        IPaystackService paystackService,
        IMonnifyService monnifyService,
        IFlutterwaveService flutterwaveService)
    {
        _context = context;
        _logger = logger;
        _featureFlagService = featureFlagService;
        _paystackService = paystackService;
        _monnifyService = monnifyService;
        _flutterwaveService = flutterwaveService;
    }

    [DisableConcurrentExecution(300)]
    public async Task ExecuteAsync(
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(
            _logger,
            "ProcessPayouts");

        if (!await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.AllowWithdrawFunds,
                cancellationToken))
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
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var payoutId in payoutIds)
        {
            await ProcessPayoutAsync(
                payoutId,
                cancellationToken);
        }

        op.Success(
            $"Processed {payoutIds.Count} payout(s).");
    }

    private async Task ProcessPayoutAsync(
        long payoutId,
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(
            _logger,
            "ProcessPayout",
            ("PayoutId", payoutId));

        var payout = await _context.Payouts
            .Include(x => x.Wallet)
            .Include(x => x.BankAccount)
            .FirstOrDefaultAsync(
                x => x.Id == payoutId,
                cancellationToken);

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
            await MarkFailedAsync(
                payout,
                "Wallet was not found.",
                cancellationToken);

            return;
        }

        if (payout.BankAccount is null)
        {
            await MarkFailedAsync(
                payout,
                "Bank account was not found.",
                cancellationToken);

            return;
        }

        if (payout.Amount.MinorUnits <= 0)
        {
            await MarkFailedAsync(
                payout,
                "Payout amount must be greater than zero.",
                cancellationToken);

            return;
        }

        var gateway = await ResolveGatewayAsync(cancellationToken);

        if (gateway is null)
        {
            await MarkFailedAsync(
                payout,
                "No payout gateway is currently enabled.",
                cancellationToken);

            return;
        }

        if (payout.Status == PayoutStatus.Processing)
        {
            await VerifyExistingTransferAsync(
                payout,
                gateway.Value,
                cancellationToken);

            return;
        }

        payout.Status = PayoutStatus.Processing;
        payout.Provider = gateway.Value.ToString();
        payout.InitiatedAt ??= DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(
            cancellationToken);

        try
        {
            var result = await SendTransferAsync(
                gateway.Value,
                payout,
                cancellationToken);

            if (!result.IsSuccessful)
            {
                await MarkFailedAsync(
                    payout,
                    result.Message,
                    cancellationToken);

                return;
            }

            payout.ProviderReference = result.Reference;

            if (result.Status.Equals(
                    "success",
                    StringComparison.OrdinalIgnoreCase))
            {
                await MarkSuccessfulAsync(
                    payout,
                    cancellationToken);

                return;
            }

            payout.Status = PayoutStatus.Processing;

            await _context.SaveChangesAsync(
                cancellationToken);

            op.Success(
                $"Payout submitted to {gateway.Value}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing payout {PayoutId} via {Gateway}.",
                payout.Id,
                gateway.Value);

            await _context.SaveChangesAsync(
                cancellationToken);

            throw;
        }
    }

    private async Task<PaymentProvider?> ResolveGatewayAsync(
        CancellationToken cancellationToken)
    {
        if (await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.PayoutsViaMonnify,
                cancellationToken))
        {
            return PaymentProvider.Monnify;
        }

        if (await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.PayoutsViaFlutterwave,
                cancellationToken))
        {
            return PaymentProvider.Flutterwave;
        }

        if (await _featureFlagService.IsEnabledAsync(
                FeatureFlagName.PayoutsViaPaystack,
                cancellationToken))
        {
            return PaymentProvider.Paystack;
        }

        return null;
    }

    private async Task<TransferResult> SendTransferAsync(
        PaymentProvider gateway,
        Payout payout,
        CancellationToken cancellationToken)
    {
        return gateway switch
        {
            PaymentProvider.Paystack =>
                await _paystackService.TransferAsync(
                    payout.BankAccount,
                    payout.Amount,
                    payout.Reference,
                    cancellationToken),

            PaymentProvider.Monnify =>
                await _monnifyService.TransferAsync(
                    payout.BankAccount,
                    payout.Amount,
                    payout.Reference,
                    cancellationToken),

            PaymentProvider.Flutterwave =>
                await _flutterwaveService.TransferAsync(
                    payout.BankAccount,
                    payout.Amount,
                    payout.Reference,
                    cancellationToken),

            _ => throw new InvalidOperationException(
                $"Unsupported gateway: {gateway}"),
        };
    }

    private async Task VerifyExistingTransferAsync(
        Payout payout,
        PaymentProvider gateway,
        CancellationToken cancellationToken)
    {
        var result = await VerifyTransferAsync(
            gateway,
            payout,
            cancellationToken);

        if (!result.IsSuccessful)
        {
            payout.FailedAttempts++;

            if (payout.FailedAttempts >= 3)
            {
                payout.Status = PayoutStatus.Failed;
                payout.FailureReason = result.Message;
                payout.FailedAt = DateTimeOffset.UtcNow;
            }

            await _context.SaveChangesAsync(
                cancellationToken);

            return;
        }

        payout.ProviderReference = result.Reference;

        switch (result.Status.ToLowerInvariant())
        {
            case "success":
                await MarkSuccessfulAsync(
                    payout,
                    cancellationToken);
                break;

            case "failed":
                await MarkFailedAsync(
                    payout,
                    result.Message,
                    cancellationToken);
                break;

            case "reversed":
                payout.Status = PayoutStatus.Reversed;
                payout.FailureReason = result.Message;
                payout.FailedAt = DateTimeOffset.UtcNow;

                await _context.SaveChangesAsync(
                    cancellationToken);
                break;

            case "pending":
            case "otp":
            case "received":
            case "processing":
                payout.Status = PayoutStatus.Processing;

                await _context.SaveChangesAsync(
                    cancellationToken);
                break;
        }
    }

    private async Task<TransferResult> VerifyTransferAsync(
        PaymentProvider gateway,
        Payout payout,
        CancellationToken cancellationToken)
    {
        return gateway switch
        {
            PaymentProvider.Paystack =>
                await _paystackService.VerifyTransferAsync(
                    payout.Reference,
                    cancellationToken),

            PaymentProvider.Monnify =>
                await _monnifyService.VerifyTransferAsync(
                    payout.Reference,
                    cancellationToken),

            PaymentProvider.Flutterwave =>
                await _flutterwaveService.VerifyTransferAsync(
                    payout.Reference,
                    cancellationToken),

            _ => throw new InvalidOperationException(
                $"Unsupported gateway: {gateway}"),
        };
    }

    private async Task MarkSuccessfulAsync(
        Payout payout,
        CancellationToken cancellationToken)
    {
        if (payout.Status == PayoutStatus.Successful)
            return;

        payout.Status = PayoutStatus.Successful;
        payout.CompletedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(
            cancellationToken);
    }

    private async Task MarkFailedAsync(
        Payout payout,
        string reason,
        CancellationToken cancellationToken)
    {
        payout.FailedAttempts++;
        payout.FailureReason = reason;

        payout.Status = payout.FailedAttempts >= 3
            ? PayoutStatus.Failed
            : PayoutStatus.Pending;

        if (payout.Status == PayoutStatus.Failed)
            payout.FailedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(
            cancellationToken);
    }
}