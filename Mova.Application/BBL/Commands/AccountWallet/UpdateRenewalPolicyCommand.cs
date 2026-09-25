using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.Shared;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class UpdateRenewalPolicyCommand
{
    public sealed class Command : IRequest<BaseResult<UpdateRenewalPolicyResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        public long WalletId { get; set; }

        public string TriggerType { get; set; } = string.Empty;

        public decimal TriggerAmount { get; set; }

        public string RefillAmountType { get; set; } = string.Empty;

        public decimal RefillAmount { get; set; }

        public decimal MinMainBalance { get; set; }

        public int? MaxRenewals { get; set; }

        public bool RefillUntilMainBalanceExhausted { get; set; }
    }

    public sealed class UpdateRenewalPolicyResponseDto
    {
        public string WalletName { get; init; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<UpdateRenewalPolicyResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly INotificationQueue _notificationQueue;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            INotificationQueue notificationQueue)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _notificationQueue = notificationQueue;
        }

        public async Task<BaseResult<UpdateRenewalPolicyResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "UpdateRenewalPolicy",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            var triggerType = request.TriggerType?
                .Trim()
                .ToLowerInvariant() switch
            {
                "oncompletion" => RenewalTriggerType.OnCompletion,
                "onthreshold" => RenewalTriggerType.OnThreshold,
                _ => (RenewalTriggerType?)null,
            };

            if (triggerType is null)
            {
                op.Fail($"Invalid trigger type: {request.TriggerType}");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid trigger type. Must be one of: oncompletion, onthreshold.");
            }

            var refillAmountType = request.RefillAmountType?
                .Trim()
                .ToLowerInvariant() switch
            {
                "fixed" => RefillAmountType.Fixed,
                "custom" => RefillAmountType.Custom,
                _ => (RefillAmountType?)null,
            };

            if (refillAmountType is null)
            {
                op.Fail($"Invalid refill amount type: {request.RefillAmountType}");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid refill amount type. Must be one of: fixed, custom.");
            }

            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId
                         && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Fail("Wallet not found.");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            var policy = await _unitOfWork.Query<RenewalPolicy>()
                .FirstOrDefaultAsync(
                    x => x.WalletId == wallet.Id,
                    cancellationToken);

            if (policy is null)
            {
                op.Fail("Renewal policy not found for this wallet.");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.NotFound,
                    "This wallet has no automation policy to update.");
            }

            if (triggerType.Value == RenewalTriggerType.OnThreshold)
            {
                if (request.TriggerAmount <= 0)
                {
                    op.Fail("Trigger amount is required for threshold trigger.");
                    return new BaseResult<UpdateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Trigger amount must be greater than zero.");
                }

                if (request.TriggerAmount >= wallet.TargetAmount.ToDecimal())
                {
                    op.Fail("Trigger amount cannot be >= wallet target.");
                    return new BaseResult<UpdateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Trigger amount must be less than the wallet's target amount.");
                }
            }

            if (refillAmountType.Value == RefillAmountType.Custom)
            {
                if (request.RefillAmount < 2000)
                {
                    op.Fail("Custom refill amount must be at least ₦2,000.");
                    return new BaseResult<UpdateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Refill amount must be at least ₦2,000.");
                }

                if (request.RefillAmount > wallet.TargetAmount.ToDecimal())
                {
                    op.Fail("Refill amount cannot exceed the wallet target.");
                    return new BaseResult<UpdateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Refill amount cannot exceed the wallet's target amount.");
                }
            }

            var effectiveMinMainBalance = request.RefillUntilMainBalanceExhausted
                ? 0m
                : request.MinMainBalance;

            if (effectiveMinMainBalance < 0)
            {
                op.Fail("Min main balance cannot be negative.");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Minimum main balance cannot be negative.");
            }

            if (request.MaxRenewals is <= 0)
            {
                op.Fail("Max renewals must be greater than zero if set.");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Max renewals must be greater than zero.");
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                policy.TriggerType = triggerType.Value;
                policy.TriggerAmount = triggerType.Value == RenewalTriggerType.OnThreshold
                    ? Money.FromNaira(request.TriggerAmount)
                    : Money.FromNaira(0);
                policy.RefillAmountType = refillAmountType.Value;
                policy.RefillAmount = refillAmountType.Value == RefillAmountType.Custom
                    ? Money.FromNaira(request.RefillAmount)
                    : Money.FromNaira(0);
                policy.MinMainBalance = Money.FromNaira(effectiveMinMainBalance);
                policy.MaxRenewals = request.MaxRenewals;
                policy.RefillUntilMainBalanceExhausted =
                    request.RefillUntilMainBalanceExhausted;
                policy.ModifiedAt = DateTimeOffset.UtcNow;

                _unitOfWork.Update(policy);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                op.Success(
                    $"Renewal policy updated. PolicyId: {policy.Id}, " +
                    $"RefillUntilExhausted: {request.RefillUntilMainBalanceExhausted}");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                _logger.LogError(
                    ex,
                    "Error updating renewal policy {PolicyId}.",
                    policy.Id);

                op.Fail($"Error updating renewal policy: {ex.Message}");

                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while updating automation.");
            }

            try
            {
                await SendRenewalPolicyUpdatedNotificationsAsync(
                    request.UserPublicId,
                    request.Email,
                    request.FirstName,
                    wallet.Id,
                    wallet.Name,
                    request.RefillUntilMainBalanceExhausted);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for updated renewal policy on wallet {WalletId}.",
                    wallet.Id);
            }

            return new BaseResult<UpdateRenewalPolicyResponseDto>(
                HttpStatusCode.OK,
                "Automation updated successfully.",
                new UpdateRenewalPolicyResponseDto
                {
                    WalletName = wallet.Name,
                });
        }

        private async Task SendRenewalPolicyUpdatedNotificationsAsync(
            string userPublicId,
            string email,
            string firstName,
            long walletId,
            string walletName,
            bool refillUntilExhausted)
        {
            var title = $"{walletName} automation updated";

            var refillBehaviourClause = refillUntilExhausted
                ? " Refills will now use whatever is available in your main balance — even if it's less than the refill amount — so long as there's something there."
                : string.Empty;

            var inAppMessage =
                $"Your automation settings for {walletName} have been updated." +
                refillBehaviourClause;

            var emailSubject = $"Automation updated for {walletName}";

            var emailMessage =
                $"Your automation settings for {walletName} have been updated. " +
                $"You can review or change them anytime from the wallet settings." +
                refillBehaviourClause;

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.Wallet,
                    title,
                    inAppMessage,
                    $"/wallet/{walletId}/automation",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for updated renewal policy on wallet {WalletId}.",
                    walletId);
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
                    "Email queue failed for updated renewal policy on wallet {WalletId}.",
                    walletId);
            }
        }
    }
}