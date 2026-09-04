using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Security;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.TransactionPin;

public sealed class ChangePinCommand
{
    public sealed class Command : IRequest<BaseResult>
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

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly ITransactionPinService _transactionPinService;
        private readonly IIdentityService _identityService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            ITransactionPinService transactionPinService,
            IIdentityService identityService,
            ILogger<Handler> logger)
        {
            _transactionPinService = transactionPinService;
            _identityService = identityService;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "ChangeTransactionPin",
                ("UserId", request.UserPublicId));

            // 1. Validate input
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Fail("UserPublicId is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.CurrentPin))
            {
                op.Fail("Current PIN is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Current PIN is required.");
            }

            if (request.CurrentPin.Length != 6)
            {
                op.Fail($"Invalid current PIN length: {request.CurrentPin.Length}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Current PIN must be exactly 6 digits.");
            }

            if (!request.CurrentPin.All(char.IsDigit))
            {
                op.Fail("Current PIN contains non-digit characters.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Current PIN must contain only digits.");
            }

            if (string.IsNullOrWhiteSpace(request.NewPin))
            {
                op.Fail("New PIN is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "New PIN is required.");
            }

            if (request.NewPin.Length != 6)
            {
                op.Fail($"Invalid new PIN length: {request.NewPin.Length}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "New PIN must be exactly 6 digits.");
            }

            if (!request.NewPin.All(char.IsDigit))
            {
                op.Fail("New PIN contains non-digit characters.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "New PIN must contain only digits.");
            }

            if (request.NewPin == request.CurrentPin)
            {
                op.Fail("New PIN cannot be the same as current PIN.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "New PIN cannot be the same as current PIN.");
            }

            // 2. Get user
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

            // 3. Check if user has a PIN
            var hasPin = await _transactionPinService.HasPinAsync(
                request.UserPublicId,
                cancellationToken);

            if (!hasPin)
            {
                op.Fail("Transaction PIN has not been set.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Transaction PIN has not been set.");
            }

            // 4. Verify current PIN
            var isCurrentPinValid = await _transactionPinService.VerifyPinAsync(
                request.UserPublicId,
                request.CurrentPin,
                cancellationToken);

            if (!isCurrentPinValid)
            {
                op.Fail("Invalid current PIN provided.");
                return new BaseResult(
                    HttpStatusCode.Unauthorized,
                    "Invalid current PIN.");
            }

            // 5. Change PIN
            try
            {
                await _transactionPinService.ChangePinAsync(
                    request.UserPublicId,
                    request.NewPin,
                    cancellationToken);

                op.Success($"Transaction PIN changed successfully for user {request.UserPublicId}");

                return new BaseResult(
                    HttpStatusCode.OK,
                    "Transaction PIN changed successfully.");
            }
            catch (ArgumentException argEx)
            {
                op.Fail($"Invalid PIN format: {argEx.Message}", argEx);
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    argEx.Message);
            }
            catch (InvalidOperationException invEx)
            {
                op.Fail($"PIN operation error: {invEx.Message}", invEx);
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    invEx.Message);
            }
            catch (Exception ex)
            {
                op.Fail($"Error changing PIN for user {request.UserPublicId}: {ex.Message}", ex);
                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while changing your transaction PIN. Please try again later.");
            }
        }
    }
}