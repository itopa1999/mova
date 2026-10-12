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
                op.Fail("UserPublicId is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Pin))
            {
                op.Fail("PIN is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "PIN is required.");
            }

            if (request.Pin.Length != 6)
            {
                op.Fail($"Invalid PIN length: {request.Pin.Length}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "PIN must be exactly 6 digits.");
            }

            if (!request.Pin.All(char.IsDigit))
            {
                op.Fail("PIN contains non-digit characters.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "PIN must contain only digits.");
            }

            if (!Enum.IsDefined(typeof(PinChallengeType), request.PinChallengeType))
            {
                op.Fail($"Invalid PinChallengeType value: {(int)request.PinChallengeType}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "A valid PIN challenge type is required.");
            }

            if (!AllowedChallengeTypes.Contains(request.PinChallengeType))
            {
                op.Fail($"Disallowed PinChallengeType for /verify-pin: {request.PinChallengeType}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Unsupported PIN challenge type for this endpoint.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                op.Fail($"User not found: {request.UserPublicId}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            try
            {
                var hasPin = await _transactionPinService.HasPinAsync(
                    request.UserPublicId,
                    cancellationToken);

                if (!hasPin)
                {
                    op.Fail($"PIN not set for user {request.UserPublicId}");
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
                            QueuePinLockoutEmail(user.FirstName, user.Email);
                        }

                        op.Fail($"Transaction PIN locked for user {request.UserPublicId}.");
                        return new BaseResult(
                            HttpStatusCode.Locked,
                            "Too many incorrect PIN attempts. Your transaction PIN is locked for 1 hour. Please try again in 1 hour.");
                    }

                    op.Fail($"Invalid PIN provided for user {request.UserPublicId}");
                    return new BaseResult(
                        HttpStatusCode.Unauthorized,
                        "Invalid transaction PIN.");
                }

                op.Success($"Transaction PIN verified successfully for user {request.UserPublicId}");

                return new BaseResult(
                    HttpStatusCode.OK,
                    "Transaction PIN verified successfully.");
            }
            catch (ArgumentException argEx)
            {
                op.Fail($"Invalid PIN format: {argEx.Message}", argEx);
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "The PIN format is invalid.");
            }
            catch (InvalidOperationException invEx)
            {
                op.Fail($"PIN operation error: {invEx.Message}", invEx);
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "The PIN operation could not be completed.");
            }
            catch (Exception ex)
            {
                op.Fail($"Error verifying PIN for user {request.UserPublicId}: {ex.Message}", ex);
                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while verifying your transaction PIN. Please try again later.");
            }
        }

        private void QueuePinLockoutEmail(string firstName, string email)
        {
            try
            {
                _notificationQueue.QueueNotificationEmail(
                    firstName,
                    email,
                    "We locked your transaction PIN for 1 hour after 3 incorrect attempts. " +
                    "Please try again in 1 hour. If you did not make these attempts, " +
                    "please contact MOVA support.",
                    "Transaction PIN temporarily locked");
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Unable to queue transaction PIN lockout email for {Email}.",
                    email);
            }
        }
    }
}