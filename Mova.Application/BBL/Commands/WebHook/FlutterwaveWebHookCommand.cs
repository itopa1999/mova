using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public FlutterwaveWebhookDataDto? Data { get; set; }
    }

    public sealed class FlutterwaveWebhookDataDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("payment_method")]
        public FlutterwavePaymentMethodDto? PaymentMethod { get; set; }
    }

    public sealed class FlutterwavePaymentMethodDto
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("bank_transfer")]
        public FlutterwaveBankTransferDto? BankTransfer { get; set; }
    }

    public sealed class FlutterwaveBankTransferDto
    {
        [JsonPropertyName("virtual_account_number")]
        public string VirtualAccountNumber { get; set; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<FlutterwaveWebhookResponseDto>>
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        private readonly IFlutterwaveService _flutterwaveService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IFlutterwaveService flutterwaveService,
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            ILogger<Handler> logger)
        {
            _flutterwaveService = flutterwaveService;
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _logger = logger;
        }

        public async Task<BaseResult<FlutterwaveWebhookResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "FlutterwaveWebHook",
                ("Signature", !string.IsNullOrWhiteSpace(request.Signature) ? "Present" : "Missing"));

            if (string.IsNullOrWhiteSpace(request.Signature))
            {
                op.Fail("Webhook signature is missing.");
                return Result(HttpStatusCode.Unauthorized, "Invalid webhook signature.");
            }

            var isValid = await _flutterwaveService.VerifyWebhookSignatureAsync(
                request.RawBody,
                request.Signature);

            if (!isValid)
            {
                op.Fail("Invalid webhook signature.");
                return Result(HttpStatusCode.Unauthorized, "Invalid webhook signature.");
            }

            FlutterwaveWebhookResponseDto? webhook;

            try
            {
                webhook = JsonSerializer.Deserialize<FlutterwaveWebhookResponseDto>(
                    request.RawBody,
                    JsonOptions);
            }
            catch (JsonException jsonEx)
            {
                op.Fail($"Invalid JSON payload: {jsonEx.Message}", jsonEx);
                return Result(HttpStatusCode.BadRequest, "Invalid webhook payload.");
            }

            if (webhook?.Data is null)
            {
                op.Fail("Webhook data is missing.");
                return Result(HttpStatusCode.BadRequest, "Invalid webhook payload.");
            }

            var data = webhook.Data;
            var bankTransfer = data.PaymentMethod?.BankTransfer;

            if (!string.Equals(webhook.Type, "charge.completed", StringComparison.OrdinalIgnoreCase))
            {
                op.Fail($"Webhook event ignored: {webhook.Type}");
                return Result(HttpStatusCode.OK, "Webhook event ignored.", webhook);
            }

            if (!string.Equals(data.Status, "succeeded", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(data.Status, "successful", StringComparison.OrdinalIgnoreCase))
            {
                op.Fail($"Transaction not successful: {data.Status}");
                return Result(HttpStatusCode.OK, "Transaction is not successful.", webhook);
            }

            if (string.IsNullOrWhiteSpace(data.Reference))
            {
                op.Fail("Transaction reference is missing.");
                return Result(HttpStatusCode.BadRequest, "Transaction reference is required.");
            }

            if (data.Amount <= 0)
            {
                op.Fail($"Invalid transaction amount: {data.Amount}");
                return Result(HttpStatusCode.BadRequest, "Invalid transaction amount.");
            }

            if (!string.Equals(data.Currency, "NGN", StringComparison.OrdinalIgnoreCase))
            {
                op.Fail($"Unsupported currency: {data.Currency}");
                return Result(HttpStatusCode.BadRequest, "Unsupported transaction currency.");
            }

            if (bankTransfer is null
                || !string.Equals(data.PaymentMethod?.Type, "bank_transfer", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(bankTransfer.VirtualAccountNumber))
            {
                op.Fail("Flutterwave virtual account details are missing.");
                return Result(HttpStatusCode.BadRequest, "Invalid virtual account transaction.");
            }

            var virtualAccount = await _unitOfWork.Query<VirtualAccount>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.AccountNumber == bankTransfer.VirtualAccountNumber
                         && x.Provider == PaymentProvider.Flutterwave
                         && x.Status == VirtualAccountStatus.Active,
                    cancellationToken);

            if (virtualAccount is null)
            {
                op.Fail($"Virtual account not found: {bankTransfer.VirtualAccountNumber}");
                return Result(HttpStatusCode.BadRequest, "Virtual account not found.");
            }

            var existingTransaction = await _unitOfWork.Query<Transaction>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Reference == data.Reference,
                    cancellationToken);

            if (existingTransaction is not null)
            {
                op.Fail($"Duplicate transaction detected: {data.Reference}");
                return Result(HttpStatusCode.OK, "Transaction already processed.", webhook);
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var updated = await _identityService.UpdateBalanceAsync(
                    virtualAccount.UserPublicId,
                    data.Amount,
                    cancellationToken);

                if (!updated)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    op.Fail($"Failed to update balance for user: {virtualAccount.UserPublicId}");
                    return Result(HttpStatusCode.BadRequest, "Failed to update user balance.");
                }

                var transaction = new Transaction
                {
                    WalletId = null,
                    Amount = Money.FromNaira(data.Amount),
                    Type = TransactionType.Deposit,
                    Status = TransactionStatus.Completed,
                    Reference = data.Reference,
                    CompletedAt = DateTimeOffset.UtcNow,
                };

                await _unitOfWork.AddAsync(transaction, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var ledgerEntry = new LedgerEntry
                {
                    WalletId = null,
                    TransactionId = transaction.Id,
                    Amount = Money.FromNaira(data.Amount),
                    IsCredit = true,
                };

                await _unitOfWork.AddAsync(ledgerEntry, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success($"Webhook processed successfully. Reference: {data.Reference}, Amount: ₦{data.Amount:N2}, User: {virtualAccount.UserPublicId}");
                return Result(HttpStatusCode.OK, "Webhook processed successfully.", webhook);
            }
            catch (DbUpdateException dbEx)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                var duplicateTransaction = await _unitOfWork.Query<Transaction>()
                    .AsNoTracking()
                    .AnyAsync(x => x.Reference == data.Reference, cancellationToken);

                if (duplicateTransaction)
                {
                    op.Fail($"Duplicate transaction detected: {data.Reference}");
                    return Result(HttpStatusCode.OK, "Transaction already processed.", webhook);
                }

                op.Fail($"Database error processing webhook: {dbEx.Message}", dbEx);
                return Result(HttpStatusCode.Conflict, "A database conflict occurred while processing the webhook.");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                op.Fail($"Error processing webhook: {ex.Message}", ex);
                return Result(HttpStatusCode.InternalServerError, "An error occurred while processing the webhook.");
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
