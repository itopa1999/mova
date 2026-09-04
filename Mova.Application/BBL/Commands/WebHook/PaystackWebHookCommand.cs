using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.WebHook;

public sealed class PaystackWebHookCommand
{
    public sealed class Command : IRequest<BaseResult<PaystackWebHookResponseDto>>
    {
        public string RawBody { get; set; } = string.Empty;

        public string? Signature { get; set; }
    }

    public sealed class PaystackWebHookResponseDto
    {
        public string Event { get; set; } = string.Empty;

        public PaystackWebhookDataDto Data { get; set; } = new();
    }

    public sealed class PaystackWebhookDataDto
    {
        public long Id { get; set; }

        public string Status { get; set; } = string.Empty;

        public string Reference { get; set; } = string.Empty;

        public long Amount { get; set; }

        public string Currency { get; set; } = string.Empty;

        public string Channel { get; set; } = string.Empty;

        public PaystackCustomerDto Customer { get; set; } = new();

        public PaystackAuthorizationDto Authorization { get; set; } = new();
    }

    public sealed class PaystackCustomerDto
    {
        public long Id { get; set; }

        public string CustomerCode { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;
    }

    public sealed class PaystackAuthorizationDto
    {
        public string Channel { get; set; } = string.Empty;

        public string? SenderBank { get; set; }

        public string? SenderBankAccountNumber { get; set; }

        public string? SenderName { get; set; }

        public string? ReceiverBankAccountNumber { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<PaystackWebHookResponseDto>>
    {
        private readonly IPaystackService _paystackService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IPaystackService paystackService,
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            ILogger<Handler> logger)
        {
            _paystackService = paystackService;
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _logger = logger;
        }

        public async Task<BaseResult<PaystackWebHookResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "PaystackWebHook",
                ("Signature", !string.IsNullOrWhiteSpace(request.Signature) ? "Present" : "Missing"));

            // 1. Validate signature
            if (string.IsNullOrWhiteSpace(request.Signature))
            {
                op.Fail("Webhook signature is missing.");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Invalid webhook signature.",
                    null);
            }

            // 2. Verify signature
            var isValid = await _paystackService.VerifyWebhookSignatureAsync(
                request.RawBody,
                request.Signature);

            if (!isValid)
            {
                op.Fail("Invalid webhook signature.");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Invalid webhook signature.",
                    null);
            }

            // 3. Deserialize payload
            PaystackWebHookResponseDto? webhook;

            try
            {
                webhook = JsonSerializer.Deserialize<PaystackWebHookResponseDto>(
                    request.RawBody);
            }
            catch (JsonException jsonEx)
            {
                op.Fail($"Invalid JSON payload: {jsonEx.Message}", jsonEx);
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid webhook payload.",
                    null);
            }

            if (webhook is null)
            {
                op.Fail("Deserialized webhook payload is null.");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid webhook payload.",
                    null);
            }

            // 4. Check event type
            if (!string.Equals(webhook.Event, "charge.success", StringComparison.OrdinalIgnoreCase))
            {
                op.Fail($"Webhook event ignored: {webhook.Event}");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Webhook event ignored.",
                    webhook);
            }

            // 5. Check transaction status
            if (!string.Equals(webhook.Data.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                op.Fail($"Transaction not successful: {webhook.Data.Status}");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Transaction is not successful.",
                    webhook);
            }

            // 6. Validate transaction data
            if (string.IsNullOrWhiteSpace(webhook.Data.Reference))
            {
                op.Fail("Transaction reference is missing.");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Transaction reference is required.",
                    null);
            }

            if (webhook.Data.Amount <= 0)
            {
                op.Fail($"Invalid transaction amount: {webhook.Data.Amount}");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid transaction amount.",
                    null);
            }

            if (!string.Equals(webhook.Data.Currency, "NGN", StringComparison.OrdinalIgnoreCase))
            {
                op.Fail($"Unsupported currency: {webhook.Data.Currency}");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Unsupported transaction currency.",
                    null);
            }

            if (!string.Equals(webhook.Data.Authorization.Channel, "dedicated_nuban", StringComparison.OrdinalIgnoreCase))
            {
                op.Fail($"Invalid channel: {webhook.Data.Authorization.Channel}");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid virtual account transaction.",
                    null);
            }

            if (string.IsNullOrWhiteSpace(webhook.Data.Authorization.ReceiverBankAccountNumber))
            {
                op.Fail("Receiver account number is missing.");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Receiver account number is missing.",
                    null);
            }

            // 7. Get virtual account
            var virtualAccount = await _unitOfWork.Query<VirtualAccount>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.AccountNumber == webhook.Data.Authorization.ReceiverBankAccountNumber
                         && x.Provider == PaymentProvider.Paystack
                         && x.Status == VirtualAccountStatus.Active,
                    cancellationToken);

            if (virtualAccount == null)
            {
                op.Fail($"Virtual account not found: {webhook.Data.Authorization.ReceiverBankAccountNumber}");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Virtual account not found.",
                    null);
            }

            // 8. Check for duplicate transaction
            var existingTransaction = await _unitOfWork.Query<Transaction>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Reference == webhook.Data.Reference,
                    cancellationToken);

            if (existingTransaction != null)
            {
                op.Fail($"Duplicate transaction detected: {webhook.Data.Reference}");
                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Transaction already processed.",
                    webhook);
            }

            // 9. Calculate amount
            var amount = webhook.Data.Amount / 100m;

            // 10. Begin transaction
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // 11. Update user balance
                var updated = await _identityService.UpdateBalanceAsync(
                    virtualAccount.UserPublicId,
                    amount,
                    cancellationToken);

                if (!updated)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail($"Failed to update balance for user: {virtualAccount.UserPublicId}");
                    return new BaseResult<PaystackWebHookResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Failed to update user balance.",
                        null);
                }

                // 12. Create transaction
                var transaction = new Transaction
                {
                    WalletId = null,
                    Amount = Money.FromNaira(amount),
                    Type = TransactionType.Deposit,
                    Status = TransactionStatus.Completed,
                    Reference = webhook.Data.Reference,
                    CompletedAt = DateTimeOffset.UtcNow,
                };

                await _unitOfWork.AddAsync(transaction, cancellationToken);

                // 13. Create ledger entry
                var ledgerEntry = new LedgerEntry
                {
                    WalletId = null,
                    TransactionId = transaction.Id,
                    Amount = Money.FromNaira(amount),
                    IsCredit = true,
                };

                await _unitOfWork.AddAsync(ledgerEntry, cancellationToken);

                // 14. Save changes
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                // 15. Commit transaction
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Webhook processed successfully. Reference: {webhook.Data.Reference}, Amount: ₦{amount:N0}, User: {virtualAccount.UserPublicId}");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Webhook processed successfully.",
                    webhook);
            }
            catch (DbUpdateException dbEx)
            {
                // Rollback immediately on database error
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Database error processing webhook: {dbEx.Message}", dbEx);

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.Conflict,
                    "A database conflict occurred while processing the webhook.",
                    null);
            }
            catch (Exception ex)
            {
                // Rollback immediately on any error
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Error processing webhook: {ex.Message}", ex);

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while processing the webhook.",
                    null);
            }
        }
    }
}