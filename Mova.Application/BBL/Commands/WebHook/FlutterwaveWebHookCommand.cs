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
    public sealed class Command : IRequest<BaseResult<FlutterwaveWebhookResponseDto>>
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
        : IRequestHandler<Command, BaseResult<FlutterwaveWebhookResponseDto>>
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

        public async Task<BaseResult<FlutterwaveWebhookResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "FlutterwaveWebHook",
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

            var isValid = await _flutterwaveService.VerifyWebhookSignatureAsync(
                request.RawBody,
                request.Signature);

            if (!isValid)
            {
                op.Warning("Invalid webhook signature.");
                return Fail(HttpStatusCode.Unauthorized, "Invalid webhook signature.");
            }

            if (!TryDeserialize(request.RawBody, out FlutterwaveWebhookResponseDto? webhook))
            {
                op.Warning("Invalid JSON payload.");
                return Fail(HttpStatusCode.BadRequest, "Invalid webhook payload.");
            }

            op.Success(
                $"Webhook received. FlutterwaveId: {webhook.Id}, TxRef: {webhook.TxRef}, " +
                $"Status: {webhook.Status}, Amount: ₦{webhook.Amount:N2}, " +
                $"Currency: {webhook.Currency}, Event: {webhook.EventType}.");

            if (!string.Equals(webhook.EventType, "BANK_TRANSFER_TRANSACTION", StringComparison.OrdinalIgnoreCase))
            {
                op.Success($"Webhook event ignored: {webhook.EventType}.");
                return Ok("Webhook event ignored.", webhook);
            }

            if (!string.Equals(webhook.Status, "successful", StringComparison.OrdinalIgnoreCase))
            {
                op.Success($"Transaction is not successful. Status: {webhook.Status}.");
                return Ok("Transaction is not successful.", webhook);
            }

            if (string.IsNullOrWhiteSpace(webhook.TxRef))
            {
                op.Warning("Transaction reference is missing.");
                return Fail(HttpStatusCode.BadRequest, "Transaction reference is required.");
            }

            if (webhook.Amount <= 0)
            {
                op.Warning($"Invalid transaction amount: {webhook.Amount}.");
                return Fail(HttpStatusCode.BadRequest, "Invalid transaction amount.");
            }

            if (!string.Equals(webhook.Currency, "NGN", StringComparison.OrdinalIgnoreCase))
            {
                op.Warning($"Unsupported currency: {webhook.Currency}.");
                return Fail(HttpStatusCode.BadRequest, "Unsupported transaction currency.");
            }

            var transaction = await _unitOfWork.Query<Transaction>()
                .FirstOrDefaultAsync(x => x.Reference == webhook.TxRef, cancellationToken);

            if (transaction is null)
            {
                op.Warning($"Transaction not found: {webhook.TxRef}.");
                return Fail(HttpStatusCode.NotFound, "Transaction not found.");
            }

            if (transaction.Status == TransactionStatus.Completed)
            {
                op.Success($"Duplicate webhook ignored. Reference: {webhook.TxRef}.");
                return Ok("Transaction already processed.", webhook);
            }

            var receivedAmountMinorUnits = Convert.ToInt64(
                Math.Round(webhook.Amount * 100m, MidpointRounding.AwayFromZero));

            if (receivedAmountMinorUnits < transaction.Amount.MinorUnits)
            {
                op.Warning(
                    $"Amount mismatch. Expected at least: {transaction.Amount.MinorUnits}, " +
                    $"Received: {receivedAmountMinorUnits}, Reference: {webhook.TxRef}.");
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

            await using var tx = await _unitOfWork.BeginAtomicSerializableAsync(cancellationToken);

            var freshTransaction = await _unitOfWork.Query<Transaction>()
                .FirstOrDefaultAsync(x => x.Reference == webhook.TxRef, cancellationToken);

            if (freshTransaction is null)
            {
                op.Warning($"Transaction disappeared inside scope: {webhook.TxRef}.");
                return Fail(HttpStatusCode.NotFound, "Transaction not found.");
            }

            if (freshTransaction.Status == TransactionStatus.Completed)
            {
                op.Success($"Duplicate webhook detected inside scope. Reference: {webhook.TxRef}.");
                return Ok("Transaction already processed.", webhook);
            }

            var updated = await _identityService.CreditBalanceAsync(
                freshTransaction.UserPublicId,
                freshTransaction.Amount.ToDecimal(),
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
                    Amount = Money.FromNaira(webhook.Amount),
                    IsCredit = true,
                },
                cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Flutterwave webhook processed. Reference: {webhook.TxRef}, " +
                $"Amount: ₦{webhook.Amount:N2}, User: {freshTransaction.UserPublicId}.");

            SendDepositNotifications(user, webhook.Amount);

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
            out FlutterwaveWebhookResponseDto? result)
        {
            try
            {
                result = JsonSerializer.Deserialize<FlutterwaveWebhookResponseDto>(body, JsonOptions);
                return result is not null;
            }
            catch (JsonException)
            {
                result = null;
                return false;
            }
        }

        private static BaseResult<FlutterwaveWebhookResponseDto> Ok(
            string message,
            FlutterwaveWebhookResponseDto? data = null)
            => new(HttpStatusCode.OK, message, data);

        private static BaseResult<FlutterwaveWebhookResponseDto> Fail(
            HttpStatusCode status,
            string message)
            => new(status, message, null);
    }
}