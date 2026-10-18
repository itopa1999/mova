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

        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Current PIN must be exactly 6 digits.")]
        public string CurrentPin { get; init; } = string.Empty;

        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "New PIN must be exactly 6 digits.")]
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
                op.Warning("UserPublicId is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Error($"Permissions unavailable for user {request.UserPublicId}.");
                return new BaseResult<object>(
                    HttpStatusCode.InternalServerError,
                    "We couldn't verify your account permissions right now. Please try again in a moment.");
            }

            if (!permissions.CanPerformSensitiveOperations)
            {
                op.Warning(
                    $"Sensitive-operation gate blocked PIN change for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from performing this operation. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<object>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

            if (string.IsNullOrWhiteSpace(request.CurrentPin))
            {
                op.Warning("Current PIN is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Current PIN is required.");
            }

            if (request.CurrentPin.Length != 6)
            {
                op.Warning($"Invalid current PIN length: {request.CurrentPin.Length}.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Current PIN must be exactly 6 digits.");
            }

            if (!request.CurrentPin.All(char.IsDigit))
            {
                op.Warning("Current PIN contains non-digit characters.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "Current PIN must contain only digits.");
            }

            if (string.IsNullOrWhiteSpace(request.NewPin))
            {
                op.Warning("New PIN is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "New PIN is required.");
            }

            if (request.NewPin.Length != 6)
            {
                op.Warning($"Invalid new PIN length: {request.NewPin.Length}.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "New PIN must be exactly 6 digits.");
            }

            if (!request.NewPin.All(char.IsDigit))
            {
                op.Warning("New PIN contains non-digit characters.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "New PIN must contain only digits.");
            }

            if (request.NewPin == request.CurrentPin)
            {
                op.Warning("New PIN matches current PIN.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "New PIN cannot be the same as current PIN.");
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {request.UserPublicId}.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            var hasPin = await _transactionPinService.HasPinAsync(
                request.UserPublicId,
                cancellationToken);

            if (!hasPin)
            {
                op.Warning("Transaction PIN not set.");
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
                op.Warning("Invalid current PIN.");
                return new BaseResult<object>(
                    HttpStatusCode.Unauthorized,
                    "Invalid current PIN.");
            }

            await _transactionPinService.ChangePinAsync(
                request.UserPublicId,
                request.NewPin,
                cancellationToken);

            op.Success($"Transaction PIN changed for user {request.UserPublicId}.");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Security,
                "Transaction PIN changed",
                "Your transaction PIN was changed successfully. " +
                "If this wasn't you, contact support immediately.",
                "/dashboard/settings",
                null,
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    user.FirstName,
                    user.Email,
                    "Your transaction PIN was changed successfully. " +
                    "If you didn't make this change, contact support immediately " +
                    "to secure your account.",
                    "Your MOVA transaction PIN was changed");
            }

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "Transaction PIN changed successfully.",
                new { notification = true });
        }
    }
}