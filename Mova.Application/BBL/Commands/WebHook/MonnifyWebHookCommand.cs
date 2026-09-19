using System.Net;
using System.Security.Cryptography;
using System.Text;
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

public sealed class MonnifyWebHookCommand
{
    public sealed class Command : IRequest<BaseResult<MonnifyWebHookResponseDto>>
    {
        public byte[] RawBody { get; init; } = Array.Empty<byte>();

        public string? Signature { get; init; }
    }

    public sealed class MonnifyWebHookResponseDto
    {
        public string EventType { get; set; } = string.Empty;

        public MonnifyEventDataDto EventData { get; set; } = new();
    }

    public sealed class MonnifyEventDataDto
    {
        public string ProductType { get; set; } = string.Empty;

        public string TransactionReference { get; set; } = string.Empty;

        public string PaymentReference { get; set; } = string.Empty;

        public decimal AmountPaid { get; set; }

        public decimal SettlementAmount { get; set; }

        public string PaymentStatus { get; set; } = string.Empty;

        public string Currency { get; set; } = string.Empty;

        public string PaymentMethod { get; set; } = string.Empty;

        public string PaidOn { get; set; } = string.Empty;

        public MonnifyCustomerDto Customer { get; set; } = new();

        public MonnifyProductDto Product { get; set; } = new();
    }

    public sealed class MonnifyCustomerDto
    {
        public string Name { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;
    }

    public sealed class MonnifyProductDto
    {
        public string Reference { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<MonnifyWebHookResponseDto>>
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        private readonly IMonnifyService _monnifyService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IMonnifyService monnifyService,
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _monnifyService = monnifyService;
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<MonnifyWebHookResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "MonnifyWebHook",
                (
                    "Signature",
                    !string.IsNullOrWhiteSpace(request.Signature)
                        ? "Present"
                        : "Missing"
                ));

            if (string.IsNullOrWhiteSpace(request.Signature))
            {
                op.Fail("Webhook signature is missing.");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Invalid webhook signature.",
                    null);
            }

            if (request.RawBody.Length == 0)
            {
                op.Fail("Webhook body is empty.");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Webhook body is empty.",
                    null);
            }

            var isValidSignature =
                await _monnifyService.VerifyWebhookSignatureAsync(
                    request.RawBody,
                    request.Signature);

            if (!isValidSignature)
            {
                op.Fail("Invalid webhook signature.");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.Unauthorized,
                    "Invalid webhook signature.",
                    null);
            }

            MonnifyWebHookResponseDto? webhook;

            try
            {
                webhook =
                    JsonSerializer.Deserialize<MonnifyWebHookResponseDto>(
                        request.RawBody,
                        JsonOptions);
            }
            catch (JsonException jsonEx)
            {
                op.Fail(
                    $"Invalid JSON payload: {jsonEx.Message}",
                    jsonEx);

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid webhook payload.",
                    null);
            }

            if (webhook is null)
            {
                op.Fail("Deserialized webhook payload is null.");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid webhook payload.",
                    null);
            }

            if (string.IsNullOrWhiteSpace(webhook.EventType))
            {
                op.Fail("Webhook event type is missing.");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Webhook event type is required.",
                    null);
            }

            // Only handle successful collections (deposits).
            if (!string.Equals(
                    webhook.EventType,
                    "SUCCESSFUL_TRANSACTION",
                    StringComparison.OrdinalIgnoreCase))
            {
                op.Success($"Webhook event ignored: {webhook.EventType}");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Webhook event ignored.",
                    webhook);
            }

            var eventData = webhook.EventData;

            if (eventData is null)
            {
                op.Fail("Webhook event data is missing.");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid webhook payload.",
                    null);
            }

            if (!string.Equals(
                    eventData.PaymentStatus,
                    "PAID",
                    StringComparison.OrdinalIgnoreCase))
            {
                op.Success(
                    $"Transaction not paid. Status: {eventData.PaymentStatus}");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Transaction is not paid.",
                    webhook);
            }

            if (string.IsNullOrWhiteSpace(eventData.PaymentReference))
            {
                op.Fail("Payment reference is missing.");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Payment reference is required.",
                    null);
            }

            if (eventData.AmountPaid <= 0)
            {
                op.Fail($"Invalid amount: {eventData.AmountPaid}");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid transaction amount.",
                    null);
            }

            if (!string.Equals(
                    eventData.Currency,
                    "NGN",
                    StringComparison.OrdinalIgnoreCase))
            {
                op.Fail($"Unsupported currency: {eventData.Currency}");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Unsupported transaction currency.",
                    null);
            }

            var transaction =
                await _unitOfWork.Query<Transaction>()
                    .FirstOrDefaultAsync(
                        x => x.Reference == eventData.PaymentReference,
                        cancellationToken);

            if (transaction is null)
            {
                op.Fail(
                    $"Transaction not found for reference: {eventData.PaymentReference}");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.NotFound,
                    "Transaction not found.",
                    null);
            }

            if (transaction.Status == TransactionStatus.Completed)
            {
                op.Success(
                    $"Duplicate webhook ignored. Reference: {eventData.PaymentReference}");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.OK,
                    "Transaction already processed.",
                    webhook);
            }

            // Monnify sends amountPaid as the total the customer paid.
            // Credit the transaction's own amount (the initiated amount)
            // so fee handling doesn't inflate the user's balance.
            if (eventData.AmountPaid < transaction.Amount.ToDecimal())
            {
                op.Fail(
                    $"Amount too low. Expected at least: {transaction.Amount.ToDecimal()}, " +
                    $"Received: {eventData.AmountPaid}, Reference: {eventData.PaymentReference}");

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Transaction amount mismatch.",
                    null);
            }

            if (eventData.AmountPaid != transaction.Amount.ToDecimal())
            {
                _logger.LogInformation(
                    "Monnify amountPaid {AmountPaid} exceeds initiated {InitiatedAmount} " +
                    "(likely customer-paid fee). Crediting initiated amount. Reference: {Reference}.",
                    eventData.AmountPaid,
                    transaction.Amount.ToDecimal(),
                    eventData.PaymentReference);
            }

            string creditedUserPublicId = string.Empty;
            var creditedAmount = transaction.Amount.ToDecimal();

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var freshTransaction =
                    await _unitOfWork.Query<Transaction>()
                        .FirstOrDefaultAsync(
                            x => x.Reference == eventData.PaymentReference,
                            cancellationToken);

                if (freshTransaction is null)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Fail("Transaction disappeared inside transaction scope.");

                    return new BaseResult<MonnifyWebHookResponseDto>(
                        HttpStatusCode.NotFound,
                        "Transaction not found.",
                        null);
                }

                if (freshTransaction.Status == TransactionStatus.Completed)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Success(
                        $"Duplicate webhook detected inside transaction. Reference: {eventData.PaymentReference}");

                    return new BaseResult<MonnifyWebHookResponseDto>(
                        HttpStatusCode.OK,
                        "Transaction already processed.",
                        webhook);
                }

                var updated =
                    await _identityService.CreditBalanceAsync(
                        freshTransaction.UserPublicId,
                        creditedAmount,
                        cancellationToken);

                if (!updated)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                    op.Fail(
                        $"Failed to update balance for user: {freshTransaction.UserPublicId}");

                    return new BaseResult<MonnifyWebHookResponseDto>(
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
                    Amount = Money.FromNaira(creditedAmount),
                    IsCredit = true,
                };

                await _unitOfWork.AddAsync(ledgerEntry, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                creditedUserPublicId = freshTransaction.UserPublicId;

                op.Success(
                    $"Monnify webhook processed. " +
                    $"Reference: {eventData.PaymentReference}, " +
                    $"Amount: ₦{creditedAmount:N2}, " +
                    $"User: {creditedUserPublicId}");
            }
            catch (DbUpdateException dbEx)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                var duplicate =
                    await _unitOfWork.Query<Transaction>()
                        .AsNoTracking()
                        .AnyAsync(
                            x => x.Reference == eventData.PaymentReference
                                 && x.Status == TransactionStatus.Completed,
                            cancellationToken);

                if (duplicate)
                {
                    op.Success(
                        $"Duplicate webhook ignored after DB constraint. Reference: {eventData.PaymentReference}");

                    return new BaseResult<MonnifyWebHookResponseDto>(
                        HttpStatusCode.OK,
                        "Transaction already processed.",
                        webhook);
                }

                op.Fail(
                    $"Database error processing Monnify webhook: {dbEx.Message}",
                    dbEx);

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.Conflict,
                    "A database conflict occurred while processing the webhook.",
                    null);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                op.Fail(
                    $"Error processing Monnify webhook: {ex.Message}",
                    ex);

                return new BaseResult<MonnifyWebHookResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while processing the webhook.",
                    null);
            }

            try
            {
                await SendDepositNotificationsAsync(
                    creditedUserPublicId,
                    creditedAmount,
                    eventData.PaymentReference);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for Monnify webhook. Reference: {Reference}",
                    eventData.PaymentReference);
            }

            return new BaseResult<MonnifyWebHookResponseDto>(
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
                    "In-app notification failed for Monnify deposit. Reference: {Reference}",
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
                    "Email queue failed for Monnify deposit. Reference: {Reference}",
                    reference);
            }
        }
    }
}