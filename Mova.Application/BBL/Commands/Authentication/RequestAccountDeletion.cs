using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.Queries.Profile;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Authentication;

public sealed class RequestAccountDeletion
{
    private const decimal MainBalanceResolutionThreshold = 100m;

    public sealed class Command : IRequest<BaseResult>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        public string? Reason { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IMediator _mediator;
        private readonly IIdentityService _identityService;
        private readonly IPinChallenge _pinChallenge;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IMediator mediator,
            IIdentityService identityService,
            IPinChallenge pinChallenge,
            IRefreshTokenService refreshTokenService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _mediator = mediator;
            _identityService = identityService;
            _pinChallenge = pinChallenge;
            _refreshTokenService = refreshTokenService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "RequestAccountDeletion",
                ("UserPublicId", request.UserPublicId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Fail("UserPublicId is required.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "User identity is required.");
            }

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.AccountDeletion,
                cancellationToken);

            if (!challengeValid)
            {
                op.Fail($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            var user = await _identityService.GetByIdentifierAsync(
                request.UserPublicId,
                cancellationToken);

            if (user is null)
            {
                op.Fail($"User not found: {request.UserPublicId}");
                return new BaseResult(
                    HttpStatusCode.NotFound,
                    "User not found.");
            }

            if (user.Balance.ToDecimal() > MainBalanceResolutionThreshold)
            {
                op.Fail(
                    $"Main balance above threshold. " +
                    $"Balance: {user.Balance.ToDecimal():N2}, " +
                    $"Threshold: {MainBalanceResolutionThreshold:N2}");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    $"Your main account balance is above ₦{MainBalanceResolutionThreshold:N0}");
            }

            var eligibility = await _mediator.Send(
                new GetRequestForAccountDeletion.Query
                {
                    UserPublicId = request.UserPublicId
                },
                cancellationToken);

            if (!eligibility.IsSuccess || eligibility.Data is null)
            {
                op.Fail(
                    $"Eligibility query failed. " +
                    $"StatusCode: {eligibility.StatusCode}, " +
                    $"Message: {eligibility.Message}");
                return new BaseResult(
                    eligibility.StatusCode,
                    eligibility.Message);
            }

            if (!eligibility.Data.CanRequestAccountDeletion)
            {
                op.Fail("Account is not eligible for deletion.");
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "Your account is not eligible for deletion yet. " +
                    "Please resolve the outstanding items first.");
            }

            var details = string.IsNullOrWhiteSpace(request.Reason)
                ? "User requested account deletion."
                : $"User requested account deletion. Reason: {request.Reason}";

            try
            {
                var closed = await _identityService.CloseAccountAsync(
                    request.UserPublicId,
                    AccountRestrictionReason.UserRequestedDeactivation,
                    details,
                    performedBy: "self-service",
                    cancellationToken);

                if (!closed)
                {
                    op.Fail($"CloseAccountAsync returned false for user {request.UserPublicId}.");
                    return new BaseResult(
                        HttpStatusCode.InternalServerError,
                        "Failed to submit your account deletion request. Please try again.");
                }
            }
            catch (Exception ex)
            {
                op.Fail($"Error closing account for user {request.UserPublicId}.", ex);
                throw;
            }

            try
            {
                var revoked = await _refreshTokenService.RevokeAllForUserAsync(
                    request.UserPublicId,
                    reason: "Account closed",
                    cancellationToken);

                op.Success(
                    $"Account closed and refresh tokens revoked. " +
                    $"RevokedTokens: {revoked}");
            }
            catch (Exception ex)
            {
                op.Fail(
                    $"Account closed but refresh token revocation failed for user {request.UserPublicId}.",
                    ex);
                throw;
            }

            await SendDeletionNotificationsAsync(
                op,
                request.UserPublicId,
                user.FirstName,
                user.Email);

            op.Success("Account deletion request completed.");

            return new BaseResult(
                HttpStatusCode.OK,
                "Your account deletion request has been submitted.");
        }

        private async Task SendDeletionNotificationsAsync(
            IDisposable op,
            string userPublicId,
            string firstName,
            string email)
        {
            var title = "Account deletion confirmed";
            var inAppMessage =
                "Your account has been closed. Thank you for using MOVA — we hope to see you again.";
            var emailSubject = "Your MOVA account has been closed";
            var emailMessage =
                "Your MOVA account has been successfully closed. " +
                "Thank you for choosing MOVA — we're grateful you gave us a try. " +
                "If you ever want to come back, our support team will be happy to help you set up a new account.";

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.Account,
                    title,
                    inAppMessage,
                    "/",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for account deletion. UserPublicId: {UserPublicId}",
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
                    "Email queue failed for account deletion. UserPublicId: {UserPublicId}",
                    userPublicId);
            }
        }
    }
}