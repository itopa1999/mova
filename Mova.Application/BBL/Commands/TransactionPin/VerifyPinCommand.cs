using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.TransactionPin;

public sealed class VerifyPinCommand
{
    private static readonly HashSet<PinChallengeType> AllowedChallengeTypes = new()
    {
        PinChallengeType.CreateWallet,
        PinChallengeType.BankAccountDeletion,
        PinChallengeType.AccountDeletion,
        PinChallengeType.AddBankAccount,
        PinChallengeType.ManualTransferToBank,
        PinChallengeType.ManualPayUtilities,
        PinChallengeType.BreakWallet,
        PinChallengeType.PausedWallet,
        PinChallengeType.UnpausedWallet,
        PinChallengeType.RestartWallet,
        PinChallengeType.LinkBankAccountToWallet,
        PinChallengeType.WalletAutomation,
    };

    public sealed class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "PIN must be exactly 6 digits.")]
        public string Pin { get; init; } = string.Empty;

        public PinChallengeType PinChallengeType { get; init; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly ITransactionPinService _transactionPinService;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            ITransactionPinService transactionPinService,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _transactionPinService = transactionPinService;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "VerifyTransactionPin",
                ("UserId", request.UserPublicId),
                ("ChallengeType", request.PinChallengeType));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Pin))
            {
                op.Warning("PIN is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "PIN is required.");
            }

            if (request.Pin.Length != 6)
            {
                op.Warning($"Invalid PIN length: {request.Pin.Length}.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "PIN must be exactly 6 digits.");
            }

            if (!request.Pin.All(char.IsDigit))
            {
                op.Warning("PIN contains non-digit characters.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "PIN must contain only digits.");
            }

            if (!Enum.IsDefined(typeof(PinChallengeType), request.PinChallengeType))
            {
                op.Warning($"Invalid PinChallengeType value: {(int)request.PinChallengeType}.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "A valid PIN challenge type is required.");
            }

            if (!AllowedChallengeTypes.Contains(request.PinChallengeType))
            {
                op.Warning($"Disallowed PinChallengeType: {request.PinChallengeType}.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Unsupported PIN challenge type for this endpoint.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {request.UserPublicId}.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            var hasPin = await _transactionPinService.HasPinAsync(
                request.UserPublicId,
                cancellationToken);

            if (!hasPin)
            {
                op.Warning($"PIN not set for user {request.UserPublicId}.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Transaction PIN has not been set.");
            }

            var verificationResult = await _transactionPinService.VerifyPinWithStatusAsync(
                request.UserPublicId,
                request.Pin,
                request.PinChallengeType,
                cancellationToken);

            if (verificationResult != TransactionPinVerificationResult.Verified)
            {
                if (verificationResult is TransactionPinVerificationResult.Locked
                    or TransactionPinVerificationResult.LockedNow)
                {
                    if (verificationResult == TransactionPinVerificationResult.LockedNow
                        && !string.IsNullOrWhiteSpace(user.Email))
                    {
                        _notificationQueue.QueueNotificationEmail(
                            user.FirstName,
                            user.Email,
                            "We locked your transaction PIN for 1 hour after 3 incorrect attempts. " +
                            "Please try again in 1 hour. If you did not make these attempts, " +
                            "please contact MOVA support.",
                            "Transaction PIN temporarily locked");
                    }

                    op.Warning($"Transaction PIN locked for user {request.UserPublicId}.");
                    return new BaseResult(
                        HttpStatusCode.Locked,
                        "Too many incorrect PIN attempts. Your transaction PIN is locked for 1 hour. Please try again in 1 hour.");
                }

                op.Warning($"Invalid PIN provided for user {request.UserPublicId}.");
                return new BaseResult(
                    HttpStatusCode.Unauthorized,
                    "Invalid transaction PIN.");
            }

            op.Success($"Transaction PIN verified for user {request.UserPublicId}.");

            return new BaseResult(
                HttpStatusCode.OK,
                "Transaction PIN verified successfully.");
        }
    }
}