using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public sealed class FlutterwaveWebHookCommand
{
    public sealed class Command
        : IRequest<BaseResult<FlutterwaveWebhookResponseDto>>
    {
        public byte[] RawBody { get; set; } = Array.Empty<byte>();

        public string? Signature { get; set; }
    }

    public sealed class FlutterwaveWebhookResponseDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("txRef")]
        public string TxRef { get; set; } = string.Empty;

        [JsonPropertyName("flwRef")]
        public string FlwRef { get; set; } = string.Empty;

        [JsonPropertyName("orderRef")]
        public string OrderRef { get; set; } = string.Empty;

        [JsonPropertyName("paymentPlan")]
        public object? PaymentPlan { get; set; }

        [JsonPropertyName("paymentPage")]
        public object? PaymentPage { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTimeOffset CreatedAt { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("charged_amount")]
        public decimal ChargedAmount { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("IP")]
        public string Ip { get; set; } = string.Empty;

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonPropertyName("appfee")]
        public decimal AppFee { get; set; }

        [JsonPropertyName("merchantfee")]
        public decimal MerchantFee { get; set; }

        [JsonPropertyName("merchantbearsfee")]
        public int MerchantBearsFee { get; set; }

        [JsonPropertyName("charge_type")]
        public string ChargeType { get; set; } = string.Empty;

        [JsonPropertyName("customer")]
        public FlutterwaveCustomerDto? Customer { get; set; }

        [JsonPropertyName("entity")]
        public FlutterwaveEntityDto? Entity { get; set; }

        [JsonPropertyName("event.type")]
        public string EventType { get; set; } = string.Empty;
    }

    public sealed class FlutterwaveCustomerDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("phone")]
        public string? Phone { get; set; }

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("customertoken")]
        public string? CustomerToken { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [JsonPropertyName("deletedAt")]
        public DateTimeOffset? DeletedAt { get; set; }

        [JsonPropertyName("AccountId")]
        public long? AccountId { get; set; }
    }

    public sealed class FlutterwaveEntityDto
    {
        [JsonPropertyName("account_number")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("first_name")]
        public string? FirstName { get; set; }

        [JsonPropertyName("last_name")]
        public string? LastName { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTimeOffset? CreatedAt { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<
            Command,
            BaseResult<FlutterwaveWebhookResponseDto>>
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        private readonly IFlutterwaveService _flutterwaveService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IFlutterwaveService flutterwaveService,
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _flutterwaveService = flutterwaveService;
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<
            BaseResult<FlutterwaveWebhookResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "FlutterwaveWebHook",
                (
                    "Signature",
                    !string.IsNullOrWhiteSpace(request.Signature)
                        ? "Present"
                        : "Missing"
                ));

            if (string.IsNullOrWhiteSpace(request.Signature))
            {
                op.Fail("Webhook signature is missing.");

                return Result(
                    HttpStatusCode.Unauthorized,
                    "Invalid webhook signature.");
            }

            if (request.RawBody.Length == 0)
            {
                op.Fail("Webhook body is empty.");

                return Result(
                    HttpStatusCode.BadRequest,
                    "Webhook body is empty.");
            }

            var isValid =
                await _flutterwaveService
                    .VerifyWebhookSignatureAsync(
                        request.RawBody,
                        request.Signature);

            if (!isValid)
            {
                op.Fail("Invalid webhook signature.");

                return Result(
                    HttpStatusCode.Unauthorized,
                    "Invalid webhook signature.");
            }

            FlutterwaveWebhookResponseDto? webhook;

            try
            {
                webhook =
                    JsonSerializer.Deserialize<
                        FlutterwaveWebhookResponseDto>(
                            request.RawBody,
                            JsonOptions);
            }
            catch (JsonException jsonEx)
            {
                op.Fail(
                    $"Invalid JSON payload: {jsonEx.Message}",
                    jsonEx);

                return Result(
                    HttpStatusCode.BadRequest,
                    "Invalid webhook payload.");
            }

            if (webhook is null)
            {
                op.Fail("Webhook payload is null.");

                return Result(
                    HttpStatusCode.BadRequest,
                    "Invalid webhook payload.");
            }

            op.Success(
                $"Webhook received. " +
                $"FlutterwaveId: {webhook.Id}, " +
                $"TxRef: {webhook.TxRef}, " +
                $"Status: {webhook.Status}, " +
                $"Amount: ₦{webhook.Amount:N2}, " +
                $"Currency: {webhook.Currency}, " +
                $"Event: {webhook.EventType}");

            if (!string.Equals(
                    webhook.EventType,
                    "BANK_TRANSFER_TRANSACTION",
                    StringComparison.OrdinalIgnoreCase))
            {
                op.Success(
                    $"Webhook event ignored: {webhook.EventType}");

                return Result(
                    HttpStatusCode.OK,
                    "Webhook event ignored.",
                    webhook);
            }

            if (!string.Equals(
                    webhook.Status,
                    "successful",
                    StringComparison.OrdinalIgnoreCase))
            {
                op.Success(
                    $"Transaction is not successful. " +
                    $"Status: {webhook.Status}");

                return Result(
                    HttpStatusCode.OK,
                    "Transaction is not successful.",
                    webhook);
            }

            if (string.IsNullOrWhiteSpace(webhook.TxRef))
            {
                op.Fail("Transaction reference is missing.");

                return Result(
                    HttpStatusCode.BadRequest,
                    "Transaction reference is required.");
            }

            if (webhook.Amount <= 0)
            {
                op.Fail(
                    $"Invalid transaction amount: {webhook.Amount}");

                return Result(
                    HttpStatusCode.BadRequest,
                    "Invalid transaction amount.");
            }

            if (!string.Equals(
                    webhook.Currency,
                    "NGN",
                    StringComparison.OrdinalIgnoreCase))
            {
                op.Fail(
                    $"Unsupported currency: {webhook.Currency}");

                return Result(
                    HttpStatusCode.BadRequest,
                    "Unsupported transaction currency.");
            }

            var transaction =
                await _unitOfWork.Query<Transaction>()
                    .FirstOrDefaultAsync(
                        x => x.Reference == webhook.TxRef,
                        cancellationToken);

            if (transaction is null)
            {
                op.Fail(
                    $"Transaction not found. " +
                    $"Reference: {webhook.TxRef}");

                return Result(
                    HttpStatusCode.NotFound,
                    "Transaction not found.");
            }

            if (transaction.Status == TransactionStatus.Completed)
            {
                op.Success(
                    $"Duplicate webhook ignored. " +
                    $"Reference: {webhook.TxRef}");

                return Result(
                    HttpStatusCode.OK,
                    "Transaction already processed.",
                    webhook);
            }

            var receivedAmountMinorUnits =
                Convert.ToInt64(
                    Math.Round(
                        webhook.Amount * 100m,
                        MidpointRounding.AwayFromZero));

            if (transaction.Amount.MinorUnits !=
                receivedAmountMinorUnits)
            {
                op.Fail(
                    $"Amount mismatch. " +
                    $"Expected: {transaction.Amount.MinorUnits}, " +
                    $"Received: {receivedAmountMinorUnits}, " +
                    $"Reference: {webhook.TxRef}");

                return Result(
                    HttpStatusCode.BadRequest,
                    "Transaction amount mismatch.");
            }

            string creditedUserPublicId = string.Empty;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var freshTransaction =
                    await _unitOfWork.Query<Transaction>()
                        .FirstOrDefaultAsync(
                            x => x.Reference == webhook.TxRef,
                            cancellationToken);

                if (freshTransaction is null)
                {
                    await _unitOfWork
                        .RollbackTransactionAsync(cancellationToken);

                    op.Fail(
                        $"Transaction not found inside " +
                        $"transaction scope. " +
                        $"Reference: {webhook.TxRef}");

                    return Result(
                        HttpStatusCode.NotFound,
                        "Transaction not found.");
                }

                if (freshTransaction.Status == TransactionStatus.Completed)
                {
                    await _unitOfWork
                        .RollbackTransactionAsync(cancellationToken);

                    op.Success(
                        $"Duplicate webhook detected inside " +
                        $"transaction. Reference: {webhook.TxRef}");

                    return Result(
                        HttpStatusCode.OK,
                        "Transaction already processed.",
                        webhook);
                }

                var updated =
                    await _identityService.UpdateBalanceAsync(
                        freshTransaction.UserPublicId,
                        webhook.Amount,
                        cancellationToken);

                if (!updated)
                {
                    await _unitOfWork
                        .RollbackTransactionAsync(cancellationToken);

                    op.Fail(
                        $"Failed to update balance. " +
                        $"User: {freshTransaction.UserPublicId}");

                    return Result(
                        HttpStatusCode.BadRequest,
                        "Failed to update user balance.");
                }

                freshTransaction.Status = TransactionStatus.Completed;

                freshTransaction.CompletedAt = DateTimeOffset.UtcNow;

                var ledgerEntry = new LedgerEntry
                {
                    WalletId = null,
                    TransactionId = freshTransaction.Id,
                    Amount = Money.FromNaira(webhook.Amount),
                    IsCredit = true,
                };

                await _unitOfWork.AddAsync(
                    ledgerEntry,
                    cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                creditedUserPublicId = freshTransaction.UserPublicId;

                op.Success(
                    $"Flutterwave webhook processed successfully. " +
                    $"Reference: {webhook.TxRef}, " +
                    $"Amount: ₦{webhook.Amount:N2}, " +
                    $"User: {creditedUserPublicId}");
            }
            catch (DbUpdateException dbEx)
            {
                await _unitOfWork
                    .RollbackTransactionAsync(cancellationToken);

                op.Fail(
                    $"Database error processing Flutterwave " +
                    $"webhook: {dbEx.Message}",
                    dbEx);

                return Result(
                    HttpStatusCode.Conflict,
                    "A database conflict occurred while processing the webhook.");
            }
            catch (Exception ex)
            {
                await _unitOfWork
                    .RollbackTransactionAsync(cancellationToken);

                op.Fail(
                    $"Error processing Flutterwave webhook: {ex.Message}",
                    ex);

                return Result(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while processing the webhook.");
            }

            try
            {
                await SendDepositNotificationsAsync(
                    creditedUserPublicId,
                    webhook.Amount,
                    webhook.TxRef);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for Flutterwave webhook. Reference: {Reference}",
                    webhook.TxRef);
            }

            return Result(
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
                    "In-app notification failed for Flutterwave deposit. Reference: {Reference}",
                    reference);
            }

            try
            {
                var user = await _identityService.GetByIdentifierAsync(
                    userPublicId,
                    CancellationToken.None);

                if (user is null || string.IsNullOrWhiteSpace(user.Email))
                {
                    _logger.LogWarning(
                        "Skipped deposit email for reference {Reference} — no user email on record.",
                        reference);
                    return;
                }

                _notificationQueue.QueueNotificationEmail(
                    user.FirstName,
                    user.Email,
                    emailMessage,
                    emailSubject);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Email queue failed for Flutterwave deposit. Reference: {Reference}",
                    reference);
            }
        }

        private static BaseResult<FlutterwaveWebhookResponseDto> Result(
            HttpStatusCode statusCode,
            string message,
            FlutterwaveWebhookResponseDto? data = null)
        {
            return new BaseResult<FlutterwaveWebhookResponseDto>(
                statusCode,
                message,
                data);
        }
    }
}