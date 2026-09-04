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

public sealed class SetPinCommand
{
    public sealed class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required]
        [MinLength(6, ErrorMessage = "PIN must be at least 6 characters.")]
        [MaxLength(6, ErrorMessage = "PIN must be exactly 6 characters.")]
        public string Pin { get; init; } = string.Empty;
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
                "SetTransactionPin",
                ("UserId", request.UserPublicId));

            // 1. Validate input
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

            // 3. Check if user already has a PIN
            try
            {
                var hasPin = await _transactionPinService.HasPinAsync(
                    request.UserPublicId,
                    cancellationToken);

                if (hasPin)
                {
                    op.Fail($"PIN already set for user {request.UserPublicId}");
                    return new BaseResult(
                        HttpStatusCode.Conflict,
                        "Transaction PIN has already been set.");
                }

                // 4. Set PIN
                await _transactionPinService.SetPinAsync(
                    request.UserPublicId,
                    request.Pin,
                    cancellationToken);

                op.Success($"Transaction PIN created successfully for user {request.UserPublicId}");

                return new BaseResult(
                    HttpStatusCode.OK,
                    "Transaction PIN created successfully.");
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
                op.Fail($"Error setting PIN for user {request.UserPublicId}: {ex.Message}", ex);
                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while setting your transaction PIN. Please try again later.");
            }
        }
    }
}