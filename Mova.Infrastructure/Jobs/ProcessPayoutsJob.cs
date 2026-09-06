// using Hangfire;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using Mova.Application.Interfaces.Payment;
// using Mova.Domain.Enums;
// using Mova.Infrastructure.Persistence;
// using Mova.Shared.Logging;

// namespace Mova.Infrastructure.Jobs;

// public sealed class ProcessPayoutsJob
// {
//     private readonly ApplicationDbContext _context;
//     private readonly ILogger<ProcessPayoutsJob> _logger;
//     private readonly IPaystackService _paystackService;

//     public ProcessPayoutsJob(
//         ApplicationDbContext context,
//         ILogger<ProcessPayoutsJob> logger,
//         IPaystackService paystackService)
//     {
//         _context = context;
//         _logger = logger;
//         _paystackService = paystackService;
//     }

//     [DisableConcurrentExecution(300)]
//     public async Task ExecuteAsync(
//         CancellationToken cancellationToken)
//     {
//         using var op = OperationLogger.Start(
//             _logger,
//             "ProcessPayouts");

//         var payoutIds = await _context.Payouts
//             .AsNoTracking()
//             .Where(x =>
//                 x.Status == PayoutStatus.Pending ||
//                 x.Status == PayoutStatus.Processing)
//             .OrderBy(x => x.Id)
//             .Select(x => x.Id)
//             .Take(100)
//             .ToListAsync(cancellationToken);

//         foreach (var payoutId in payoutIds)
//         {
//             await ProcessPayoutAsync(
//                 payoutId,
//                 cancellationToken);
//         }

//         op.Success(
//             $"Processed {payoutIds.Count} payout(s).");
//     }

//     private async Task ProcessPayoutAsync(
//         long payoutId,
//         CancellationToken cancellationToken)
//     {
//         using var op = OperationLogger.Start(
//             _logger,
//             "ProcessPayout",
//             ("PayoutId", payoutId));

//         var payout = await _context.Payouts
//             .Include(x => x.Wallet)
//             .Include(x => x.BankAccount)
//             .FirstOrDefaultAsync(
//                 x => x.Id == payoutId,
//                 cancellationToken);

//         if (payout is null)
//             return;

//         if (payout.Status is
//             PayoutStatus.Successful or
//             PayoutStatus.Failed or
//             PayoutStatus.Reversed)
//         {
//             return;
//         }

//         if (payout.Wallet is null)
//         {
//             await MarkFailedAsync(
//                 payout,
//                 "Wallet was not found.",
//                 cancellationToken);

//             return;
//         }

//         if (payout.BankAccount is null)
//         {
//             await MarkFailedAsync(
//                 payout,
//                 "Bank account was not found.",
//                 cancellationToken);

//             return;
//         }

//         if (payout.Amount.MinorUnits <= 0)
//         {
//             await MarkFailedAsync(
//                 payout,
//                 "Payout amount must be greater than zero.",
//                 cancellationToken);

//             return;
//         }

//         if (payout.Status == PayoutStatus.Processing)
//         {
//             await VerifyExistingTransferAsync(
//                 payout,
//                 cancellationToken);

//             return;
//         }

//         payout.Status = PayoutStatus.Processing;

//         await _context.SaveChangesAsync(
//             cancellationToken);

//         try
//         {
//             var result = await _paystackService.TransferAsync(
//                 payout.BankAccount,
//                 payout.Amount,
//                 payout.Reference,
//                 cancellationToken);

//             if (!result.IsSuccessful)
//             {
//                 await MarkFailedAsync(
//                     payout,
//                     result.Message,
//                     cancellationToken);

//                 return;
//             }

//             payout.ProviderReference = result.Reference;

//             if (result.Status.Equals(
//                     "success",
//                     StringComparison.OrdinalIgnoreCase))
//             {
//                 await MarkSuccessfulAsync(
//                     payout,
//                     cancellationToken);

//                 return;
//             }

//             payout.Status = PayoutStatus.Processing;

//             await _context.SaveChangesAsync(
//                 cancellationToken);

//             op.Success("Payout submitted to provider.");
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(
//                 ex,
//                 "Error processing payout {PayoutId}.",
//                 payout.Id);

//             await _context.SaveChangesAsync(
//                 cancellationToken);

//             throw;
//         }
//     }

//     private async Task VerifyExistingTransferAsync(
//         dynamic payout,
//         CancellationToken cancellationToken)
//     {
//         var result = await _paystackService
//             .VerifyTransferAsync(
//                 payout.Reference,
//                 cancellationToken);

//         if (!result.IsSuccessful)
//         {
//             payout.FailedAttempts++;

//             if (payout.FailedAttempts >= 3)
//             {
//                 payout.Status = PayoutStatus.Failed;
//                 payout.FailureReason = result.Message;
//             }

//             await _context.SaveChangesAsync(
//                 cancellationToken);

//             return;
//         }

//         payout.ProviderReference = result.Reference;

//         switch (result.Status.ToLowerInvariant())
//         {
//             case "success":

//                 await MarkSuccessfulAsync(
//                     payout,
//                     cancellationToken);

//                 break;

//             case "failed":

//                 await MarkFailedAsync(
//                     payout,
//                     result.Message,
//                     cancellationToken);

//                 break;

//             case "reversed":

//                 payout.Status = PayoutStatus.Reversed;
//                 payout.FailureReason = result.Message;

//                 await _context.SaveChangesAsync(
//                     cancellationToken);

//                 break;

//             case "pending":
//             case "otp":
//             case "received":

//                 payout.Status = PayoutStatus.Processing;

//                 await _context.SaveChangesAsync(
//                     cancellationToken);

//                 break;
//         }
//     }

//     private async Task MarkSuccessfulAsync(
//         dynamic payout,
//         CancellationToken cancellationToken)
//     {
//         if (payout.Status == PayoutStatus.Successful)
//             return;

//         payout.Status = PayoutStatus.Successful;
//         payout.ProcessedAt = DateTimeOffset.UtcNow;

//         await _context.SaveChangesAsync(
//             cancellationToken);
//     }

//     private async Task MarkFailedAsync(
//         dynamic payout,
//         string reason,
//         CancellationToken cancellationToken)
//     {
//         payout.FailedAttempts++;
//         payout.FailureReason = reason;

//         payout.Status = payout.FailedAttempts >= 3
//             ? PayoutStatus.Failed
//             : PayoutStatus.Pending;

//         await _context.SaveChangesAsync(
//             cancellationToken);
//     }
// }