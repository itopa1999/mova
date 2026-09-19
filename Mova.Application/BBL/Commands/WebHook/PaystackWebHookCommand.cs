using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
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
        public byte[] RawBody { get; init; } = Array.Empty<byte>();

        public string? Signature { get; init; }
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
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        private readonly IPaystackService _paystackService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IPaystackService paystackService,
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _paystackService = paystackService;
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<PaystackWebHookResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "PaystackWebHook",
                (
                    "Signature",
                    !string.IsNullOrWhiteSpace(request.Signature)
                        ? "Present"
                        : "Missing"
                ));

            if (string.IsNullOrWhiteSpace(request.Signature))
            {
                op.Fail("Webhook signature is missing.");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Invalid webhook signature.",
                    null);
            }

            if (request.RawBody.Length == 0)
            {
                op.Fail("Webhook body is empty.");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Webhook body is empty.",
                    null);
            }

            var isValidSignature =
                await _paystackService.VerifyWebhookSignatureAsync(
                    request.RawBody,
                    request.Signature);

            if (!isValidSignature)
            {
                op.Fail("Invalid webhook signature.");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Invalid webhook signature.",
                    null);
            }

            PaystackWebHookResponseDto? webhook;

            try
            {
                webhook =
                    JsonSerializer.Deserialize<PaystackWebHookResponseDto>(
                        request.RawBody,
                        JsonOptions);
            }
            catch (JsonException jsonEx)
            {
                op.Fail(
                    $"Invalid JSON payload: {jsonEx.Message}",
                    jsonEx);

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

            if (string.IsNullOrWhiteSpace(webhook.Event))
            {
                op.Fail("Webhook event is missing.");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Webhook event is required.",
                    null);
            }

            if (!string.Equals(
                    webhook.Event,
                    "charge.success",
                    StringComparison.OrdinalIgnoreCase))
            {
                op.Success($"Webhook event ignored: {webhook.Event}");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Webhook event ignored.",
                    webhook);
            }

            var webhookData = webhook.Data;

            if (webhookData is null)
            {
                op.Fail("Webhook data is missing.");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid webhook payload.",
                    null);
            }

            if (!string.Equals(
                    webhookData.Status,
                    "success",
                    StringComparison.OrdinalIgnoreCase))
            {
                op.Success(
                    $"Charge event received but status is not successful: {webhookData.Status}");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Transaction is not successful.",
                    webhook);
            }

            if (string.IsNullOrWhiteSpace(webhookData.Reference))
            {
                op.Fail("Transaction reference is missing.");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Transaction reference is required.",
                    null);
            }

            if (webhookData.Amount <= 0)
            {
                op.Fail(
                    $"Invalid transaction amount: {webhookData.Amount}");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid transaction amount.",
                    null);
            }

            var amount = webhookData.Amount / 100m;

            if (amount <= 0)
            {
                op.Fail($"Invalid converted amount: {amount}");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid transaction amount.",
                    null);
            }

            if (!string.Equals(
                    webhookData.Currency,
                    "NGN",
                    StringComparison.OrdinalIgnoreCase))
            {
                op.Fail(
                    $"Unsupported currency: {webhookData.Currency}");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Unsupported transaction currency.",
                    null);
            }

            var transaction =
                await _unitOfWork.Query<Transaction>()
                    .FirstOrDefaultAsync(
                        x => x.Reference == webhookData.Reference,
                        cancellationToken);

            if (transaction is null)
            {
                op.Fail(
                    $"Transaction not found for reference: {webhookData.Reference}");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.NotFound,
                    "Transaction not found.",
                    null);
            }

            if (transaction.Status == TransactionStatus.Completed)
            {
                op.Success(
                    $"Duplicate webhook ignored. Reference: {webhookData.Reference}");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Transaction already processed.",
                    webhook);
            }

            if (transaction.Amount.MinorUnits != webhookData.Amount)
            {
                op.Fail(
                    $"Amount mismatch. Expected: {transaction.Amount.MinorUnits}, " +
                    $"Received: {webhookData.Amount}, Reference: {webhookData.Reference}");

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Transaction amount mismatch.",
                    null);
            }

            string creditedUserPublicId = string.Empty;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var freshTransaction =
                    await _unitOfWork.Query<Transaction>()
                        .FirstOrDefaultAsync(
                            x => x.Reference == webhookData.Reference,
                            cancellationToken);

                if (freshTransaction is null)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Fail("Transaction disappeared inside transaction scope.");

                    return new BaseResult<PaystackWebHookResponseDto>(
                        HttpStatusCode.NotFound,
                        "Transaction not found.",
                        null);
                }

                if (freshTransaction.Status == TransactionStatus.Completed)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Success(
                        $"Duplicate webhook detected inside transaction. Reference: {webhookData.Reference}");

                    return new BaseResult<PaystackWebHookResponseDto>(
                        HttpStatusCode.OK,
                        "Transaction already processed.",
                        webhook);
                }

                var updated =
                    await _identityService.CreditBalanceAsync(
                        freshTransaction.UserPublicId,
                        amount,
                        cancellationToken);

                if (!updated)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Fail(
                        $"Failed to update balance for user: {freshTransaction.UserPublicId}");

                    return new BaseResult<PaystackWebHookResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Failed to update user balance.",
                        null);
                }

                freshTransaction.Status = TransactionStatus.Completed;

                freshTransaction.CompletedAt = DateTimeOffset.UtcNow;

                var ledgerEntry = new LedgerEntry
                {
                    WalletId = null,
                    TransactionId = freshTransaction.Id,
                    Amount = Money.FromNaira(amount),
                    IsCredit = true,
                };

                await _unitOfWork.AddAsync(ledgerEntry, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                creditedUserPublicId = freshTransaction.UserPublicId;

                op.Success(
                    $"Paystack webhook processed. " +
                    $"Reference: {webhookData.Reference}, " +
                    $"Amount: ₦{amount:N2}, " +
                    $"User: {creditedUserPublicId}");
            }
            catch (DbUpdateException dbEx)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                var duplicate =
                    await _unitOfWork.Query<Transaction>()
                        .AsNoTracking()
                        .AnyAsync(
                            x => x.Reference == webhookData.Reference
                                 && x.Status == TransactionStatus.Completed,
                            cancellationToken);

                if (duplicate)
                {
                    op.Success(
                        $"Duplicate webhook ignored after DB constraint. Reference: {webhookData.Reference}");

                    return new BaseResult<PaystackWebHookResponseDto>(
                        HttpStatusCode.OK,
                        "Transaction already processed.",
                        webhook);
                }

                op.Fail(
                    $"Database error processing Paystack webhook: {dbEx.Message}",
                    dbEx);

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.Conflict,
                    "A database conflict occurred while processing the webhook.",
                    null);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                op.Fail(
                    $"Error processing Paystack webhook: {ex.Message}",
                    ex);

                return new BaseResult<PaystackWebHookResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while processing the webhook.",
                    null);
            }

            try
            {
                await SendDepositNotificationsAsync(
                    creditedUserPublicId,
                    amount,
                    webhookData.Reference);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for Paystack webhook. Reference: {Reference}",
                    webhookData.Reference);
            }

            return new BaseResult<PaystackWebHookResponseDto>(
                HttpStatusCode.OK,
                "Webhook processed successfully.",
                webhook);
        }

        private async Task SendDepositNotificationsAsync(
            string userPublicId,
            decimal amount,
            string reference)
        {
            var title = "Deposit successful";

            var inAppMessage =
                $"₦{amount:N0} has been added to your available balance.";

            var emailSubject = "Your MOVA deposit is complete";

            var emailMessage =
                $"₦{amount:N0} has been added to your MOVA available balance. " +
                $"You can now allocate it to wallets or use it as you wish.";

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.Deposit,
                    title,
                    inAppMessage,
                    "/add-funds?tab=history",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for Paystack deposit. Reference: {Reference}",
                    reference);
            }

            string firstName = string.Empty;
            string email = string.Empty;

            try
            {
                var user = await _identityService.GetByIdentifierAsync(
                    userPublicId,
                    CancellationToken.None);

                if (user is null)
                {
                    _logger.LogWarning(
                        "Skipped deposit email for reference {Reference} — user not found: {UserPublicId}",
                        reference,
                        userPublicId);
                    return;
                }

                firstName = user.FirstName;
                email = user.Email;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to load user for deposit email. Reference: {Reference}, UserPublicId: {UserPublicId}",
                    reference,
                    userPublicId);
                return;
            }

            try
            {
                _notificationQueue.QueueNotificationEmail(
                    firstName,
                    email,
                    emailMessage,
                    emailSubject);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Email queue failed for Paystack deposit. Reference: {Reference}",
                    reference);
            }
        }
    }
}