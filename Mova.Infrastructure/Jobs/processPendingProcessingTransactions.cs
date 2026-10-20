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
    private static readonly TimeSpan NotFoundGracePeriod = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(30);

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
            if (cancellationToken.IsCancellationRequested)
            {
                op.Success($"Cancelled after processing {processed} transaction(s).");
                return;
            }

            try
            {
                await ProcessTransactionAsync(id, cancellationToken);
                processed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                op.Success($"Cancelled after processing {processed} transaction(s).");
                return;
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

        var provider = transaction.Provider;

        if (provider is null)
        {
            op.Success("No provider recorded. Skipping.");
            return;
        }

        var result = await SafeVerifyAsync(provider.Value, reference, cancellationToken);

        if (result is null || !result.CallSucceeded)
        {
            op.Success("Verification call failed. Leaving as-is for retry.");
            return;
        }

        switch (result.Status)
        {
            case TransactionStatus.Completed:
                if (result.Found)
                {
                    transaction.Status = TransactionStatus.Completed;
                    transaction.CompletedAt = DateTimeOffset.UtcNow;
                    transaction.FailureReason = null;

                    await _context.SaveChangesAsync(cancellationToken);
                    op.Success("Marked completed.");
                }
                else
                {
                    op.Success("Status Completed but Found=false — leaving as-is.");
                }
                return;

            case TransactionStatus.Reversed:
                if (result.Found)
                {
                    transaction.Status = TransactionStatus.Reversed;
                    transaction.FailureReason =
                        result.Message ?? "Payment was reversed at gateway.";

                    await _context.SaveChangesAsync(cancellationToken);
                    op.Success("Marked reversed.");
                }
                return;

            case TransactionStatus.Failed:
            case TransactionStatus.Abandoned:
                if (result.Found)
                {
                    transaction.Status = result.Status;
                    transaction.FailureReason =
                        result.Message ?? "Payment failed or was abandoned at gateway.";

                    await _context.SaveChangesAsync(cancellationToken);
                    op.Success($"Marked {result.Status}.");
                }
                return;

            case TransactionStatus.Pending:
            case TransactionStatus.Processing:
                if (!result.Found)
                {
                    await HandleNotFoundAsync(transaction, op, cancellationToken);
                    return;
                }

                op.Success("Still pending at gateway.");
                return;
        }
    }

    private async Task HandleNotFoundAsync(
        Domain.Entities.Transaction transaction,
        OperationLogger op,
        CancellationToken cancellationToken)
    {
        var age = DateTimeOffset.UtcNow - transaction.CreatedAt;

        if (age < NotFoundGracePeriod)
        {
            op.Success(
                $"Not found at provider, but transaction is only {age.TotalMinutes:F0}m old. Waiting.");
            return;
        }

        transaction.Status = TransactionStatus.Failed;
        transaction.FailureReason =
            "Transaction was not found at the payment provider after the grace period.";

        await _context.SaveChangesAsync(cancellationToken);
        op.Success("Marked failed (not found at provider after grace period).");
    }

    private async Task<PaymentVerificationResult?> SafeVerifyAsync(
        PaymentProvider provider,
        string reference,
        CancellationToken cancellationToken)
    {
        // Bound each provider call so one hung gateway cannot stall the whole batch.
        using var timeoutCts = new CancellationTokenSource(ProviderTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCts.Token);

        try
        {
            return provider switch
            {
                PaymentProvider.Paystack => await _paystackService
                    .VerifyPaymentAsync(reference, linkedCts.Token),

                PaymentProvider.Monnify => await _monnifyService
                    .VerifyPaymentAsync(reference, linkedCts.Token),

                PaymentProvider.Flutterwave => await _flutterwaveService
                    .VerifyPaymentAsync(reference, linkedCts.Token),

                _ => null,
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Verify call for {Provider} timed out after {Timeout}.",
                provider,
                ProviderTimeout);
            return null;
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