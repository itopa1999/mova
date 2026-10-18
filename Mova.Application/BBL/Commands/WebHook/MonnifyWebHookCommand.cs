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

            var isValidSignature = await _monnifyService.VerifyWebhookSignatureAsync(
                request.RawBody,
                request.Signature);

            if (!isValidSignature)
            {
                op.Warning("Invalid webhook signature.");
                return Fail(HttpStatusCode.Unauthorized, "Invalid webhook signature.");
            }

            if (!TryDeserialize(request.RawBody, out MonnifyWebHookResponseDto? webhook))
            {
                op.Warning("Invalid JSON payload.");
                return Fail(HttpStatusCode.BadRequest, "Invalid webhook payload.");
            }

            if (string.IsNullOrWhiteSpace(webhook.EventType))
            {
                op.Warning("Webhook event type is missing.");
                return Fail(HttpStatusCode.BadRequest, "Webhook event type is required.");
            }

            if (!string.Equals(webhook.EventType, "SUCCESSFUL_TRANSACTION", StringComparison.OrdinalIgnoreCase))
            {
                op.Success($"Webhook event ignored: {webhook.EventType}.");
                return Ok("Webhook event ignored.", webhook);
            }

            var eventData = webhook.EventData;

            if (!string.Equals(eventData.PaymentStatus, "PAID", StringComparison.OrdinalIgnoreCase))
            {
                op.Success($"Transaction not paid. Status: {eventData.PaymentStatus}.");
                return Ok("Transaction is not paid.", webhook);
            }

            if (string.IsNullOrWhiteSpace(eventData.PaymentReference))
            {
                op.Warning("Payment reference is missing.");
                return Fail(HttpStatusCode.BadRequest, "Payment reference is required.");
            }

            if (eventData.AmountPaid <= 0)
            {
                op.Warning($"Invalid amount: {eventData.AmountPaid}.");
                return Fail(HttpStatusCode.BadRequest, "Invalid transaction amount.");
            }

            if (!string.Equals(eventData.Currency, "NGN", StringComparison.OrdinalIgnoreCase))
            {
                op.Warning($"Unsupported currency: {eventData.Currency}.");
                return Fail(HttpStatusCode.BadRequest, "Unsupported transaction currency.");
            }

            var transaction = await _unitOfWork.Query<Transaction>()
                .FirstOrDefaultAsync(x => x.Reference == eventData.PaymentReference, cancellationToken);

            if (transaction is null)
            {
                op.Warning($"Transaction not found: {eventData.PaymentReference}.");
                return Fail(HttpStatusCode.NotFound, "Transaction not found.");
            }

            if (transaction.Status == TransactionStatus.Completed)
            {
                op.Success($"Duplicate webhook ignored. Reference: {eventData.PaymentReference}.");
                return Ok("Transaction already processed.", webhook);
            }

            if (eventData.AmountPaid < transaction.Amount.ToDecimal())
            {
                op.Warning(
                    $"Amount too low. Expected at least: {transaction.Amount.ToDecimal()}, " +
                    $"Received: {eventData.AmountPaid}, Reference: {eventData.PaymentReference}.");
                return Fail(HttpStatusCode.BadRequest, "Transaction amount mismatch.");
            }

            if (eventData.AmountPaid != transaction.Amount.ToDecimal())
            {
                op.Success(
                    $"AmountPaid {eventData.AmountPaid} exceeds initiated " +
                    $"{transaction.Amount.ToDecimal()} (customer-paid fee). " +
                    $"Crediting initiated amount. Reference: {eventData.PaymentReference}.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                transaction.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {transaction.UserPublicId}.");
                return Fail(HttpStatusCode.NotFound, "User not found.");
            }

            var creditedAmount = transaction.Amount.ToDecimal();

            await using var tx = await _unitOfWork.BeginAtomicSerializableAsync(cancellationToken);

            var freshTransaction = await _unitOfWork.Query<Transaction>()
                .FirstOrDefaultAsync(x => x.Reference == eventData.PaymentReference, cancellationToken);

            if (freshTransaction is null)
            {
                op.Warning($"Transaction disappeared inside scope: {eventData.PaymentReference}.");
                return Fail(HttpStatusCode.NotFound, "Transaction not found.");
            }

            if (freshTransaction.Status == TransactionStatus.Completed)
            {
                op.Success($"Duplicate webhook detected inside scope. Reference: {eventData.PaymentReference}.");
                return Ok("Transaction already processed.", webhook);
            }

            var updated = await _identityService.CreditBalanceAsync(
                freshTransaction.UserPublicId,
                creditedAmount,
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
                    Amount = Money.FromNaira(creditedAmount),
                    IsCredit = true,
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Monnify webhook processed. Reference: {eventData.PaymentReference}, " +
                $"Amount: ₦{creditedAmount:N2}, User: {freshTransaction.UserPublicId}.");

            SendDepositNotifications(user, creditedAmount);

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
                "/dashboard/add-funds?tab=history",
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
            out MonnifyWebHookResponseDto? result)
        {
            try
            {
                result = JsonSerializer.Deserialize<MonnifyWebHookResponseDto>(body, JsonOptions);
                return result is not null;
            }
            catch (JsonException)
            {
                result = null;
                return false;
            }
        }

        private static BaseResult<MonnifyWebHookResponseDto> Ok(
            string message,
            MonnifyWebHookResponseDto? data = null)
            => new(HttpStatusCode.OK, message, data);

        private static BaseResult<MonnifyWebHookResponseDto> Fail(
            HttpStatusCode status,
            string message)
            => new(status, message, null);
    }
}