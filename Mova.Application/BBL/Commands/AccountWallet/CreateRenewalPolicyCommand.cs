using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class CreateRenewalPolicyCommand
{
    public sealed class Command : IRequest<BaseResult<CreateRenewalPolicyResponseDto>>
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

    public sealed class CreateRenewalPolicyResponseDto
    {
        public string WalletName { get; init; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<CreateRenewalPolicyResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ISchedulePreviewService _schedulePreviewService;
        private readonly IPinChallenge _pinChallenge;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ISchedulePreviewService schedulePreviewService,
            IPinChallenge pinChallenge)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _schedulePreviewService = schedulePreviewService;
            _pinChallenge = pinChallenge;
        }

        public async Task<BaseResult<CreateRenewalPolicyResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "CreateRenewalPolicy",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Fail("UserPublicId is required.");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User identity is required.");
            }

            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Fail($"Permissions unavailable for user {request.UserPublicId}.");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "We couldn't verify your account permissions right now. Please try again in a moment.");
            }

            if (!permissions.CanPerformSensitiveOperations)
            {
                op.Fail(
                    $"Sensitive-operation gate blocked CreateRenewalPolicy for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, " +
                    $"Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from performing this operation. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<CreateRenewalPolicyResponseDto>(
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
                op.Fail($"Invalid trigger type: {request.TriggerType}");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
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
                return new BaseResult<CreateRenewalPolicyResponseDto>(
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
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Status is WalletStatus.Closed
                or WalletStatus.Paused
                or WalletStatus.Broken)
            {
                op.Fail($"Wallet in invalid status for automation: {wallet.Status}");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet cannot have an automation policy in its current state.");
            }

            var existingPolicy = await _unitOfWork.Query<RenewalPolicy>()
                .FirstOrDefaultAsync(
                    x => x.WalletId == wallet.Id,
                    cancellationToken);

            if (existingPolicy is not null)
            {
                op.Fail("Renewal policy already exists for this wallet.");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet already has an automation policy.");
            }

            var walletRule = await _unitOfWork.Query<WalletRule>()
                .FirstOrDefaultAsync(
                    x => x.WalletId == wallet.Id,
                    cancellationToken);

            if (walletRule is null)
            {
                op.Fail("Wallet rule not found.");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet has no release schedule to automate.");
            }

            if (triggerType.Value == RenewalTriggerType.OnThreshold)
            {
                if (request.TriggerAmount <= 0)
                {
                    op.Fail("Trigger amount is required for threshold trigger.");
                    return new BaseResult<CreateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Trigger amount must be greater than zero.");
                }

                if (request.TriggerAmount >= wallet.TargetAmount.ToDecimal())
                {
                    op.Fail("Trigger amount cannot be >= wallet target.");
                    return new BaseResult<CreateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Trigger amount must be less than the wallet's target amount.");
                }
            }

            if (refillAmountType.Value == RefillAmountType.Custom)
            {
                if (request.RefillAmount < 2000)
                {
                    op.Fail("Custom refill amount must be at least ₦2,000.");
                    return new BaseResult<CreateRenewalPolicyResponseDto>(
                        HttpStatusCode.BadRequest,
                        "Refill amount must be at least ₦2,000.");
                }

                if (request.RefillAmount > wallet.TargetAmount.ToDecimal())
                {
                    op.Fail("Refill amount cannot exceed the wallet target.");
                    return new BaseResult<CreateRenewalPolicyResponseDto>(
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
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Minimum main balance cannot be negative.");
            }

            if (request.MaxRenewals is <= 0)
            {
                op.Fail("Max renewals must be greater than zero if set.");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Max renewals must be greater than zero.");
            }

            var refillAmountEffective =
                refillAmountType.Value == RefillAmountType.Fixed
                    ? wallet.TargetAmount.ToDecimal()
                    : request.RefillAmount;

            var releaseAmountValue = walletRule.Amount.ToDecimal();

            if (releaseAmountValue <= 0)
            {
                op.Fail("Wallet rule has an invalid release amount.");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet's release amount is invalid.");
            }

            var releases = (int)Math.Ceiling(refillAmountEffective / releaseAmountValue);

            if (releases <= 0)
            {
                op.Fail("Could not determine release count for this wallet.");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Unable to determine the wallet's release schedule.");
            }

            // ─── PIN challenge validation ───
            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.WalletAutomation,
                cancellationToken);

            if (!challengeValid)
            {
                op.Fail($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            long policyId = 0;

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var policy = new RenewalPolicy
                {
                    WalletId = wallet.Id,
                    UserPublicId = request.UserPublicId,
                    IsEnabled = true,
                    TriggerType = triggerType.Value,
                    TriggerAmount = triggerType.Value == RenewalTriggerType.OnThreshold
                        ? Money.FromNaira(request.TriggerAmount)
                        : Money.FromNaira(0),
                    RefillAmountType = refillAmountType.Value,
                    RefillAmount = refillAmountType.Value == RefillAmountType.Custom
                        ? Money.FromNaira(request.RefillAmount)
                        : Money.FromNaira(0),
                    MinMainBalance = Money.FromNaira(effectiveMinMainBalance),
                    MaxRenewals = request.MaxRenewals,
                    RenewalsCount = 0,
                    Status = RenewalStatus.Active,

                    RefillUntilMainBalanceExhausted =
                        request.RefillUntilMainBalanceExhausted,
                };

                await _unitOfWork.AddAsync(policy, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                policyId = policy.Id;

                op.Success(
                    $"Renewal policy created. PolicyId: {policyId}, " +
                    $"WalletId: {wallet.Id}, " +
                    $"RefillUntilExhausted: {request.RefillUntilMainBalanceExhausted}");
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);

                _logger.LogError(
                    ex,
                    "Error creating renewal policy for wallet {WalletId}.",
                    wallet.Id);

                op.Fail($"Error creating renewal policy: {ex.Message}");

                return new BaseResult<CreateRenewalPolicyResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while enabling automation.");
            }

            try
            {
                await SendRenewalPolicyCreatedNotificationsAsync(
                    request.UserPublicId,
                    request.Email,
                    request.FirstName,
                    wallet.Id,
                    wallet.Name,
                    triggerType.Value,
                    request.RefillUntilMainBalanceExhausted);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for renewal policy {PolicyId}.",
                    policyId);
            }

            return new BaseResult<CreateRenewalPolicyResponseDto>(
                HttpStatusCode.Created,
                "Automation enabled successfully.",
                new CreateRenewalPolicyResponseDto
                {
                    WalletName = wallet.Name,
                });
        }

        private async Task SendRenewalPolicyCreatedNotificationsAsync(
            string userPublicId,
            string email,
            string firstName,
            long walletId,
            string walletName,
            RenewalTriggerType triggerType,
            bool refillUntilExhausted)
        {
            var title = $"Automation enabled for {walletName}";

            var triggerClause = triggerType switch
            {
                RenewalTriggerType.OnCompletion =>
                    "MOVA will refill this wallet automatically when it completes.",
                RenewalTriggerType.OnThreshold =>
                    "MOVA will refill this wallet automatically when its balance drops below your threshold.",
                _ => "MOVA will refill this wallet automatically."
            };

            var refillBehaviourClause = refillUntilExhausted
                ? " Refills will use whatever is available in your main balance — even if it's less than the refill amount — so long as there's something there."
                : string.Empty;

            var inAppMessage =
                $"{triggerClause}{refillBehaviourClause} You can pause or edit this anytime.";

            var emailSubject = $"Automation is on for {walletName}";

            var emailMessage =
                $"{triggerClause}{refillBehaviourClause} " +
                $"You can pause or edit this automation anytime from your wallet settings.";

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.Wallet,
                    title,
                    inAppMessage,
                    $"/wallet/{walletId}",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for renewal policy on wallet {WalletId}.",
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
                    "Email queue failed for renewal policy on wallet {WalletId}.",
                    walletId);
            }
        }
    }
}