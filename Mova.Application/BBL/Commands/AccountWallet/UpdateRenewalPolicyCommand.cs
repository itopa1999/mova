using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
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

        [Range(1, long.MaxValue)]
        public long WalletId { get; set; }

        [Required, RegularExpression(@"(?i)^\s*(oncompletion|onthreshold)\s*$", ErrorMessage = "Trigger type must be oncompletion or onthreshold.")]
        public string TriggerType { get; set; } = string.Empty;

        [Range(typeof(decimal), "0", "100000000")]
        public decimal TriggerAmount { get; set; }

        [Required, RegularExpression(@"(?i)^\s*(fixed|custom)\s*$", ErrorMessage = "Refill amount type must be fixed or custom.")]
        public string RefillAmountType { get; set; } = string.Empty;

        [Range(typeof(decimal), "0", "100000000")]
        public decimal RefillAmount { get; set; }

        [Range(typeof(decimal), "0", "92233720368547758.07")]
        public decimal MinMainBalance { get; set; }

        [Range(1, int.MaxValue)]
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
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly IPinChallenge _pinChallenge;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
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

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User identity is required.");
            }

            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Error($"Permissions unavailable for user {request.UserPublicId}.");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "We couldn't verify your account permissions right now. Please try again in a moment.");
            }

            if (!permissions.CanPerformSensitiveOperations)
            {
                op.Warning(
                    $"Sensitive-operation gate blocked UpdateRenewalPolicy for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, " +
                    $"Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from performing this operation. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

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
                op.Warning($"Invalid trigger type: {request.TriggerType}");
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
                op.Warning($"Invalid refill amount type: {request.RefillAmountType}");
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
                op.Warning("Wallet not found.");
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
                op.Warning("Renewal policy not found for this wallet.");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.NotFound,
                    "This wallet has no automation policy to update.");
            }

            if (triggerType == RenewalTriggerType.OnThreshold)
            {
                if (request.TriggerAmount <= 0)
                {
                    op.Warning("Trigger amount is required for threshold trigger.");
                    return new BaseResult<UpdateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Trigger amount must be greater than zero.");
                }

                if (request.TriggerAmount >= wallet.TargetAmount.ToDecimal())
                {
                    op.Warning("Trigger amount cannot be >= wallet target.");
                    return new BaseResult<UpdateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Trigger amount must be less than the wallet's target amount.");
                }
            }

            if (refillAmountType == RefillAmountType.Custom)
            {
                if (request.RefillAmount < 2000)
                {
                    op.Warning("Custom refill amount must be at least ₦2,000.");
                    return new BaseResult<UpdateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Refill amount must be at least ₦2,000.");
                }

                if (request.RefillAmount > wallet.TargetAmount.ToDecimal())
                {
                    op.Warning("Refill amount cannot exceed the wallet target.");
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
                op.Warning("Min main balance cannot be negative.");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Minimum main balance cannot be negative.");
            }

            if (request.MaxRenewals is <= 0)
            {
                op.Warning("Max renewals must be greater than zero if set.");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Max renewals must be greater than zero.");
            }

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.WalletAutomation,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<UpdateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            policy.TriggerType = triggerType.Value;
            policy.TriggerAmount = triggerType == RenewalTriggerType.OnThreshold
                ? Money.FromNaira(request.TriggerAmount)
                : Money.FromNaira(0);
            policy.RefillAmountType = refillAmountType.Value;
            policy.RefillAmount = refillAmountType == RefillAmountType.Custom
                ? Money.FromNaira(request.RefillAmount)
                : Money.FromNaira(0);
            policy.MinMainBalance = Money.FromNaira(effectiveMinMainBalance);
            policy.MaxRenewals = request.MaxRenewals;
            policy.RefillUntilMainBalanceExhausted = request.RefillUntilMainBalanceExhausted;
            policy.ModifiedAt = DateTimeOffset.UtcNow;

            _unitOfWork.Update(policy);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Renewal policy updated. PolicyId: {policy.Id}, " +
                $"RefillUntilExhausted: {request.RefillUntilMainBalanceExhausted}");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                $"{wallet.Name} automation updated",
                BuildInAppMessage(wallet.Name, request.RefillUntilMainBalanceExhausted),
                $"/dashboard/scheduled/wallet/{wallet.Id}/automation",
                null,
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    request.FirstName,
                    request.Email,
                    BuildEmailMessage(wallet.Name, request.RefillUntilMainBalanceExhausted),
                    $"Automation updated for {wallet.Name}");
            }

            return new BaseResult<UpdateRenewalPolicyResponseDto>(
                HttpStatusCode.OK,
                "Automation updated successfully.",
                new UpdateRenewalPolicyResponseDto
                {
                    WalletName = wallet.Name,
                });
        }

        private static string BuildInAppMessage(
            string walletName,
            bool refillUntilExhausted)
        {
            var refillBehaviourClause = refillUntilExhausted
                ? " Refills will now use whatever is available in your main balance — even if it's less than the refill amount — so long as there's something there."
                : string.Empty;

            return $"Your automation settings for {walletName} have been updated." +
                   refillBehaviourClause;
        }

        private static string BuildEmailMessage(
            string walletName,
            bool refillUntilExhausted)
        {
            var refillBehaviourClause = refillUntilExhausted
                ? " Refills will now use whatever is available in your main balance — even if it's less than the refill amount — so long as there's something there."
                : string.Empty;

            return $"Your automation settings for {walletName} have been updated. " +
                   "You can review or change them anytime from the wallet settings." +
                   refillBehaviourClause;
        }
    }
}