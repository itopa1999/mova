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

public sealed class ChangePinCommand
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required]
        [MinLength(6, ErrorMessage = "Current PIN must be at least 6 characters.")]
        [MaxLength(6, ErrorMessage = "Current PIN must be exactly 6 characters.")]
        public string CurrentPin { get; init; } = string.Empty;

        [Required]
        [MinLength(6, ErrorMessage = "New PIN must be at least 6 characters.")]
        [MaxLength(6, ErrorMessage = "New PIN must be exactly 6 characters.")]
        public string NewPin { get; init; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<object>>
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

        public async Task<BaseResult<object>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "ChangeTransactionPin",
                ("UserId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Fail("UserPublicId is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.CurrentPin))
            {
                op.Fail("Current PIN is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Current PIN is required.");
            }

            if (request.CurrentPin.Length != 6)
            {
                op.Fail($"Invalid current PIN length: {request.CurrentPin.Length}");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Current PIN must be exactly 6 digits.");
            }

            if (!request.CurrentPin.All(char.IsDigit))
            {
                op.Fail("Current PIN contains non-digit characters.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Current PIN must contain only digits.");
            }

            if (string.IsNullOrWhiteSpace(request.NewPin))
            {
                op.Fail("New PIN is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "New PIN is required.");
            }

            if (request.NewPin.Length != 6)
            {
                op.Fail($"Invalid new PIN length: {request.NewPin.Length}");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "New PIN must be exactly 6 digits.");
            }

            if (!request.NewPin.All(char.IsDigit))
            {
                op.Fail("New PIN contains non-digit characters.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "New PIN must contain only digits.");
            }

            if (request.NewPin == request.CurrentPin)
            {
                op.Fail("New PIN cannot be the same as current PIN.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "New PIN cannot be the same as current PIN.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user == null)
            {
                op.Fail($"User not found: {request.UserPublicId}");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            var hasPin = await _transactionPinService.HasPinAsync(
                request.UserPublicId,
                cancellationToken);

            if (!hasPin)
            {
                op.Fail("Transaction PIN has not been set.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Transaction PIN has not been set.");
            }

            var isCurrentPinValid = await _transactionPinService.VerifyPinAsync(
                request.UserPublicId,
                request.CurrentPin,
                cancellationToken);

            if (!isCurrentPinValid)
            {
                op.Fail("Invalid current PIN provided.");
                return new BaseResult<object>(
                    HttpStatusCode.Unauthorized,
                    "Invalid current PIN.");
            }

            try
            {
                await _transactionPinService.ChangePinAsync(
                    request.UserPublicId,
                    request.NewPin,
                    cancellationToken);

                op.Success(
                    $"Transaction PIN changed successfully for user {request.UserPublicId}");
            }
            catch (ArgumentException argEx)
            {
                op.Fail($"Invalid PIN format: {argEx.Message}", argEx);
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "The PIN format is invalid.");
            }
            catch (InvalidOperationException invEx)
            {
                op.Fail($"PIN operation error: {invEx.Message}", invEx);
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "The PIN operation could not be completed.");
            }
            catch (Exception ex)
            {
                op.Fail(
                    $"Error changing PIN for user {request.UserPublicId}: {ex.Message}",
                    ex);

                return new BaseResult<object>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while changing your transaction PIN. Please try again later.");
            }

            try
            {
                await SendPinChangedNotificationsAsync(
                    request.UserPublicId,
                    user.Email,
                    user.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for PIN change. UserPublicId: {UserPublicId}",
                    request.UserPublicId);
            }

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "Transaction PIN changed successfully.",
                new
                {
                    notification = true,
                });
        }

        private async Task SendPinChangedNotificationsAsync(
            string userPublicId,
            string email,
            string firstName)
        {
            var title = "Transaction PIN changed";

            var inAppMessage =
                "Your transaction PIN was changed successfully. " +
                "If this wasn't you, contact support immediately.";

            var emailSubject = "Your MOVA transaction PIN was changed";

            var emailMessage =
                "Your transaction PIN was changed successfully. " +
                "If you didn't make this change, contact support immediately " +
                "to secure your account.";

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.Security,
                    title,
                    inAppMessage,
                    "/settings",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for PIN change. UserPublicId: {UserPublicId}",
                    userPublicId);
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
                    "Email queue failed for PIN change. UserPublicId: {UserPublicId}",
                    userPublicId);
            }
        }
    }
}