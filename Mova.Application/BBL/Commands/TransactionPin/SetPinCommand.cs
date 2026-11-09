using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;
using Mova.Shared.Logging;
using Mova.Shared.Common.Results;
namespace Mova.Application.BBL.Commands.TransactionPin;

public sealed class SetPinCommand
{
    public sealed class Command : IRequest<BaseResult<SetPinResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "PIN must be exactly 6 digits.")]
        public string Pin { get; init; } = string.Empty;
    }

    public sealed class SetPinResponseDto
    {
        public bool IsOnboarding { get; set; }
        public bool Notification { get; set; }
        public string CurrentStage { get; set; } = string.Empty;
        public int CurrentStageValue { get; set; }
        public string NextStage { get; set; } = string.Empty;
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<SetPinResponseDto>>
    {
        private readonly ITransactionPinService _transactionPinService;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;

        public Handler(
            ITransactionPinService transactionPinService,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger)
        {
            _transactionPinService = transactionPinService;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<BaseResult<SetPinResponseDto>> Handle(
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
                return new BaseResult<SetPinResponseDto>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Pin))
            {
                op.Warning("PIN is required.");
                return new BaseResult<SetPinResponseDto>(
                    HttpStatusCode.BadRequest,
                    "PIN is required.");
            }

            if (request.Pin.Length != 6)
            {
                op.Warning($"Invalid PIN length: {request.Pin.Length}.");
                return new BaseResult<SetPinResponseDto>(
                    HttpStatusCode.BadRequest,
                    "PIN must be exactly 6 digits.");
            }

            if (!request.Pin.All(char.IsDigit))
            {
                op.Warning("PIN contains non-digit characters.");
                return new BaseResult<SetPinResponseDto>(
                    HttpStatusCode.BadRequest,
                    "PIN must contain only digits.");
            }

            // "Get me" — resolve the current user (includes OnboardingStage).
            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Warning($"User not found: {request.UserPublicId}.");
                return new BaseResult<SetPinResponseDto>(
                    HttpStatusCode.BadRequest,
                    "User not found.");
            }

            var hasPin = await _transactionPinService.HasPinAsync(
                request.UserPublicId,
                cancellationToken);

            if (hasPin)
            {
                op.Warning($"PIN already set for user {request.UserPublicId}.");
                return new BaseResult<SetPinResponseDto>(
                    HttpStatusCode.Conflict,
                    "Transaction PIN has already been set.");
            }

            var isOnboarding = user.OnboardingStage < OnboardingStage.Pin;

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            await _transactionPinService.SetPinAsync(
                request.UserPublicId,
                request.Pin,
                isOnboarding,
                cancellationToken);
  
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Transaction PIN created for user {request.UserPublicId}. " +
                $"IsOnboarding: {isOnboarding}, Stage: {OnboardingStage.Pin}.");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Security,
                "Transaction PIN created",
                "Your transaction PIN was created successfully. " +
                "You'll need it to confirm sensitive actions.",
                "/dashboard/settings",
                null,
                CancellationToken.None);

            return new BaseResult<SetPinResponseDto>(
                HttpStatusCode.OK,
                "Transaction PIN created successfully.",
                new SetPinResponseDto
                {
                    IsOnboarding = isOnboarding,
                    Notification = true,
                    CurrentStage = OnboardingStage.Pin.ToString(),
                    CurrentStageValue = (int)OnboardingStage.Pin,
                    NextStage = OnboardingStage.Agreement.ToString(),
                });
        }
    }
}