using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.Interfaces.Identity;
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
        private readonly IMediator _mediator;
        private readonly ILogger<Handler> _logger;

        public Handler(
            ITransactionPinService transactionPinService,
            IIdentityService identityService,
            IMediator mediator,
            ILogger<Handler> logger)
        {
            _transactionPinService = transactionPinService;
            _identityService = identityService;
            _mediator = mediator;
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

                // 👇 Notify the user that their PIN was changed
                await _mediator.Send(
                    new CreateNotificationCommand.Command
                    {
                        UserPublicId = request.UserPublicId,
                        Type = NotificationType.Security,
                        Title = "Transaction PIN changed",
                        Message =
                            "Your transaction PIN was changed successfully. " +
                            "If this wasn't you, contact support immediately.",
                        ActionUrl = "/settings",
                    },
                    cancellationToken);

                op.Success(
                    $"Transaction PIN changed successfully for user {request.UserPublicId}");

                return new BaseResult<object>(
                    HttpStatusCode.OK,
                    "Transaction PIN changed successfully.",
                    new
                    {
                        notification = true,
                    });
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
        }
    }
}