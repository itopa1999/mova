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

public sealed class SetPinCommand
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "PIN must be exactly 6 digits.")]
        public string Pin { get; init; } = string.Empty;
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
                "SetTransactionPin",
                ("UserId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Pin))
            {
                op.Warning("PIN is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "PIN is required.");
            }

            if (request.Pin.Length != 6)
            {
                op.Warning($"Invalid PIN length: {request.Pin.Length}.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "PIN must be exactly 6 digits.");
            }

            if (!request.Pin.All(char.IsDigit))
            {
                op.Warning("PIN contains non-digit characters.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "PIN must contain only digits.");
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

            if (hasPin)
            {
                op.Warning($"PIN already set for user {request.UserPublicId}.");
                return new BaseResult<object>(
                    HttpStatusCode.Conflict,
                    "Transaction PIN has already been set.");
            }

            await _transactionPinService.SetPinAsync(
                request.UserPublicId,
                request.Pin,
                cancellationToken);

            op.Success($"Transaction PIN created for user {request.UserPublicId}.");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Security,
                "Transaction PIN created",
                "Your transaction PIN was created successfully. " +
                "You'll need it to confirm sensitive actions.",
                "/dashboard/settings",
                null,
                CancellationToken.None);

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "Transaction PIN created successfully.",
                new { notification = true });
        }
    }
}