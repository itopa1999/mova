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
                ("Signature", string.IsNullOrWhiteSpace(request.Signature) ? "Missing" : "Present"));

            if (string.IsNullOrWhiteSpace(request.Signature))
            {
                op.Warning("Webhook signature is missing.");
                return Fail(HttpStatusCode.Unauthorized, "Invalid webhook signature.");
            }

            if (request.RawBody.Length == 0)
            {
                op.Warning("Webhook body is empty.");
                return Fail(HttpStatusCode.BadRequest, "Webhook body is empty.");
            }

            var isValidSignature = await _paystackService.VerifyWebhookSignatureAsync(
                request.RawBody,
                request.Signature);

            if (!isValidSignature)
            {
                op.Warning("Invalid webhook signature.");
                return Fail(HttpStatusCode.Unauthorized, "Invalid webhook signature.");
            }

            if (!TryDeserialize(request.RawBody, out PaystackWebHookResponseDto? webhook))
            {
                op.Warning("Invalid JSON payload.");
                return Fail(HttpStatusCode.BadRequest, "Invalid webhook payload.");
            }

            if (string.IsNullOrWhiteSpace(webhook.Event))
            {
                op.Warning("Webhook event is missing.");
                return Fail(HttpStatusCode.BadRequest, "Webhook event is required.");
            }

            if (!string.Equals(webhook.Event, "charge.success", StringComparison.OrdinalIgnoreCase))
            {
                op.Success($"Webhook event ignored: {webhook.Event}.");
                return Ok("Webhook event ignored.", webhook);
            }

            var webhookData = webhook.Data;

            if (!string.Equals(webhookData.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                op.Success($"Charge event received but status is not successful: {webhookData.Status}.");
                return Ok("Transaction is not successful.", webhook);
            }

            if (string.IsNullOrWhiteSpace(webhookData.Reference))
            {
                op.Warning("Transaction reference is missing.");
                return Fail(HttpStatusCode.BadRequest, "Transaction reference is required.");
            }

            if (webhookData.Amount <= 0)
            {
                op.Warning($"Invalid transaction amount: {webhookData.Amount}.");
                return Fail(HttpStatusCode.BadRequest, "Invalid transaction amount.");
            }

            if (!string.Equals(webhookData.Currency, "NGN", StringComparison.OrdinalIgnoreCase))
            {
                op.Warning($"Unsupported currency: {webhookData.Currency}.");
                return Fail(HttpStatusCode.BadRequest, "Unsupported transaction currency.");
            }

            var transaction = await _unitOfWork.Query<Transaction>()
                .FirstOrDefaultAsync(x => x.Reference == webhookData.Reference, cancellationToken);

            if (transaction is null)
            {
                op.Warning($"Transaction not found: {webhookData.Reference}.");
                return Fail(HttpStatusCode.NotFound, "Transaction not found.");
            }

            if (transaction.Status == TransactionStatus.Completed)
            {
                op.Success($"Duplicate webhook ignored. Reference: {webhookData.Reference}.");
                return Ok("Transaction already processed.", webhook);
            }

            if (webhookData.Amount < transaction.Amount.MinorUnits)
            {
                op.Warning(
                    $"Amount mismatch. Expected at least: {transaction.Amount.MinorUnits}, " +
                    $"Received: {webhookData.Amount}, Reference: {webhookData.Reference}.");
                return Fail(HttpStatusCode.BadRequest, "Transaction amount mismatch.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                transaction.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {transaction.UserPublicId}.");
                return Fail(HttpStatusCode.NotFound, "User not found.");
            }

            var amount = transaction.Amount.ToDecimal();

            await using var tx = await _unitOfWork.BeginAtomicSerializableAsync(cancellationToken);

            var freshTransaction = await _unitOfWork.Query<Transaction>()
                .FirstOrDefaultAsync(x => x.Reference == webhookData.Reference, cancellationToken);

            if (freshTransaction is null)
            {
                op.Warning($"Transaction disappeared inside scope: {webhookData.Reference}.");
                return Fail(HttpStatusCode.NotFound, "Transaction not found.");
            }

            if (freshTransaction.Status == TransactionStatus.Completed)
            {
                op.Success($"Duplicate webhook detected inside scope. Reference: {webhookData.Reference}.");
                return Ok("Transaction already processed.", webhook);
            }

            var updated = await _identityService.CreditBalanceAsync(
                freshTransaction.UserPublicId,
                amount,
                cancellationToken);

            if (!updated)
            {
                op.Warning($"CreditBalanceAsync returned false for user {freshTransaction.UserPublicId}.");
                return Fail(HttpStatusCode.BadRequest, "Failed to update user balance.");
            }

            freshTransaction.Status = TransactionStatus.Completed;
            freshTransaction.CompletedAt = DateTimeOffset.UtcNow;

            await _unitOfWork.AddAsync(
                new LedgerEntry
                {
                    WalletId = null,
                    TransactionId = freshTransaction.Id,
                    Amount = Money.FromNaira(amount),
                    IsCredit = true,
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Paystack webhook processed. Reference: {webhookData.Reference}, " +
                $"Amount: ₦{amount:N2}, User: {freshTransaction.UserPublicId}.");

            SendDepositNotifications(user, amount);

            return Ok("Webhook processed successfully.", webhook);
        }

        private void SendDepositNotifications(UserIdentityDto user, decimal amount)
        {
            var inAppMessage = $"₦{amount:N0} has been added to your available balance.";

            _notificationQueue.InAppNotificationAsync(
                user.PublicId,
                NotificationType.Deposit,
                "Deposit successful",
                inAppMessage,
                "/add-funds?tab=history",
                null,
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    user.FirstName,
                    user.Email,
                    $"₦{amount:N0} has been added to your MOVA available balance. " +
                    "You can now allocate it to wallets or use it as you wish.",
                    "Your MOVA deposit is complete");
            }
        }

        private static bool TryDeserialize(
            byte[] body,
            out PaystackWebHookResponseDto? result)
        {
            try
            {
                result = JsonSerializer.Deserialize<PaystackWebHookResponseDto>(body, JsonOptions);
                return result is not null;
            }
            catch (JsonException)
            {
                result = null;
                return false;
            }
        }

        private static BaseResult<PaystackWebHookResponseDto> Ok(
            string message,
            PaystackWebHookResponseDto? data = null)
            => new(HttpStatusCode.OK, message, data);

        private static BaseResult<PaystackWebHookResponseDto> Fail(
            HttpStatusCode status,
            string message)
            => new(status, message, null);
    }
}