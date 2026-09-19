using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class ProcessPendingProcessingTransactions
{
    private static readonly TimeSpan NotFoundGracePeriod =
        TimeSpan.FromMinutes(30);

    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessPendingProcessingTransactions> _logger;
    private readonly IPaystackService _paystackService;
    private readonly IMonnifyService _monnifyService;
    private readonly IFlutterwaveService _flutterwaveService;

    public ProcessPendingProcessingTransactions(
        ApplicationDbContext context,
        ILogger<ProcessPendingProcessingTransactions> logger,
        IPaystackService paystackService,
        IMonnifyService monnifyService,
        IFlutterwaveService flutterwaveService)
    {
        _context = context;
        _logger = logger;
        _paystackService = paystackService;
        _monnifyService = monnifyService;
        _flutterwaveService = flutterwaveService;
    }

    [DisableConcurrentExecution(300)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(
            _logger,
            "ProcessPendingProcessingTransactions");

        var transactionIds = await _context.Transactions
            .AsNoTracking()
            .Where(x =>
                x.Status == TransactionStatus.Pending ||
                x.Status == TransactionStatus.Processing)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .Take(500)
            .ToListAsync(cancellationToken);

        if (transactionIds.Count == 0)
        {
            op.Success("No pending or processing transactions.");
            return;
        }

        var processed = 0;

        foreach (var id in transactionIds)
        {
            try
            {
                await ProcessTransactionAsync(id, cancellationToken);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error processing transaction {TransactionId}.",
                    id);
            }
        }

        op.Success($"Processed {processed} transaction(s).");
    }

    private async Task ProcessTransactionAsync(
        long transactionId,
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(
            _logger,
            "ProcessTransaction",
            ("TransactionId", transactionId));

        var transaction = await _context.Transactions
            .FirstOrDefaultAsync(x => x.Id == transactionId, cancellationToken);

        if (transaction is null)
            return;

        if (transaction.Status is not (
            TransactionStatus.Pending or
            TransactionStatus.Processing))
        {
            return;
        }

        var reference = transaction.Reference;

        if (string.IsNullOrWhiteSpace(reference))
        {
            transaction.Status = TransactionStatus.Failed;
            transaction.FailureReason = "Transaction reference is missing.";
            await _context.SaveChangesAsync(cancellationToken);
            op.Success("Failed: missing reference.");
            return;
        }

        // Use the stored provider. If it's missing (legacy rows), fall back
        // to asking every gateway — otherwise skip the transaction entirely
        // and leave it for the next run.
        var provider = transaction.Provider;

        if (provider is null)
        {
            op.Success("No provider recorded. Skipping.");
            return;
        }

        PaymentVerificationResult? result;

        result = await SafeVerifyAsync(
            provider.Value,
            reference,
            cancellationToken);

        if (result is null)
        {
            op.Success($"Provider '{provider}' verify call failed. Leaving as-is.");
            return;
        }

        if (!result.IsSuccessful)
        {
            op.Success("Verification did not succeed. Leaving as-is.");
            return;
        }

        // 1. Provider reports success → mark completed.
        if (result.Found && result.Status == "success")
        {
            transaction.Status = TransactionStatus.Completed;
            transaction.CompletedAt = DateTimeOffset.UtcNow;
            transaction.FailureReason = null;

            await _context.SaveChangesAsync(cancellationToken);
            op.Success("Marked completed.");
            return;
        }

        // 2. Provider reports a hard failure → mark failed.
        if (result.Found && result.Status == "failed")
        {
            transaction.Status = TransactionStatus.Failed;
            transaction.FailureReason =
                result.Message ?? "Payment failed at gateway.";

            await _context.SaveChangesAsync(cancellationToken);
            op.Success("Marked failed (gateway reported failure).");
            return;
        }

        // 3. Provider has never seen this reference → mark failed,
        //    but only after the grace period to let users finish checkout.
        if (!result.Found)
        {
            // TODO uncomment this 
            // var age = DateTimeOffset.UtcNow - transaction.CreatedAt;

            // if (age < NotFoundGracePeriod)
            // {
            //     op.Success(
            //         $"Not found, but transaction is only {age.TotalMinutes:F0}m old. Waiting.");
            //     return;
            // }

            transaction.Status = TransactionStatus.Failed;
            transaction.FailureReason =
                "Transaction was not found at the payment provider.";

            await _context.SaveChangesAsync(cancellationToken);
            op.Success("Marked failed (not found at provider after grace period).");
            return;
        }

        op.Success("Still pending.");
    }

    private async Task<PaymentVerificationResult?> SafeVerifyAsync(
        PaymentProvider provider,
        string reference,
        CancellationToken cancellationToken)
    {
        try
        {
            return provider switch
            {
                PaymentProvider.Paystack => await _paystackService
                    .VerifyPaymentAsync(reference, cancellationToken),

                PaymentProvider.Monnify => await _monnifyService
                    .VerifyPaymentAsync(reference, cancellationToken),

                PaymentProvider.Flutterwave => await _flutterwaveService
                    .VerifyPaymentAsync(reference, cancellationToken),

                _ => null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Payment verification failed for provider {Provider}.",
                provider);
            return null;
        }
    }
}