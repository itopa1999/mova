using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWalletTrustee;

public sealed class ResendTrusteeInvitationCommand
{
    public sealed class Command : IRequest<BaseResult<ResendTrusteeInvitationResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long TrusteeId { get; set; }
    }

    public sealed class ResendTrusteeInvitationResponseDto
    {
        public long TrusteeId { get; init; }
        public string TrusteeEmail { get; init; } = string.Empty;
        public DateTimeOffset NewExpiresAt { get; init; }
        public DateTimeOffset SentAt { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<ResendTrusteeInvitationResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly INotificationQueue _notificationQueue;
        private readonly IPinChallenge _pinChallenge;
        private readonly TrusteeOptions _trusteeOptions;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge,
            IOptions<TrusteeOptions> trusteeOptions)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
            _trusteeOptions = trusteeOptions.Value;
        }

        public async Task<BaseResult<ResendTrusteeInvitationResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "ResendTrusteeInvitation",
                ("UserId", request.UserPublicId),
                ("TrusteeId", request.TrusteeId));

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.CreateWalletTrustee,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<ResendTrusteeInvitationResponseDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            var trustee = await _unitOfWork.Query<WalletTrustee>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.TrusteeId &&
                         x.OwnerPublicId == request.UserPublicId,
                    cancellationToken);

            if (trustee is null)
            {
                op.Warning("Trustee not found for owner.");
                return new BaseResult<ResendTrusteeInvitationResponseDto>(
                    HttpStatusCode.NotFound,
                    "Trustee not found.");
            }

            if (trustee.Status == WalletTrusteeStatus.Verified)
            {
                op.Warning($"Cannot resend — trustee already verified. Id: {trustee.Id}");
                return new BaseResult<ResendTrusteeInvitationResponseDto>(
                    HttpStatusCode.Conflict,
                    "This trustee has already accepted the invitation.");
            }

            var rawToken = TrusteeTokenHelper.Generate();
            var tokenHash = TrusteeTokenHelper.Hash(rawToken);
            var newExpiresAt = DateTimeOffset.UtcNow.AddHours(_trusteeOptions.InvitationExpiryHours);

            var rowsAffected = await _unitOfWork.Query<WalletTrustee>()
                .Where(x =>
                    x.Id == trustee.Id &&
                    x.Status == WalletTrusteeStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.VerificationTokenHash, tokenHash)
                        .SetProperty(x => x.TokenExpiresAt, newExpiresAt),
                    cancellationToken);

            if (rowsAffected == 0)
            {
                op.Warning(
                    "Race lost — trustee accepted or was deleted between read and update.");
                return new BaseResult<ResendTrusteeInvitationResponseDto>(
                    HttpStatusCode.Gone,
                    "This invitation is no longer pending. It may have been accepted or removed.");
            }

            var consentUrl = $"{_trusteeOptions.ConsentBaseUrl.TrimEnd('/')}/{rawToken}";
            var trusteeDisplayName = string.IsNullOrWhiteSpace(trustee.Name)
                ? trustee.Email.Split('@')[0]
                : trustee.Name;

            var ownerName = string.IsNullOrWhiteSpace(request.FirstName)
                ? "The wallet owner"
                : request.FirstName;

            _notificationQueue.QueueNotificationEmail(
                trusteeDisplayName,
                trustee.Email,
                $"{ownerName} has resent their invitation for you to be their trustee on MOVA.\n\n" +
                $"As a trustee, you will be asked to approve or reject money releases from " +
                $"wallets they assign to you. No money moves without your approval.\n\n" +
                $"To accept or decline this invitation, open the link below:\n{consentUrl}\n\n" +
                $"This link expires in {_trusteeOptions.InvitationExpiryHours} hours. Any " +
                $"previous invitation link you received is no longer valid — use this one.",
                $"{ownerName} resent their trustee invitation on MOVA");

            op.Success(
                $"Invitation resent. TrusteeId: {trustee.Id}, Email: {trustee.Email}, " +
                $"NewExpiresAt: {newExpiresAt:u}");

            return new BaseResult<ResendTrusteeInvitationResponseDto>(
                HttpStatusCode.OK,
                $"Invitation resent. Your trustee will have {_trusteeOptions.InvitationExpiryHours} hours to respond.",
                new ResendTrusteeInvitationResponseDto
                {
                    TrusteeId = trustee.Id,
                    TrusteeEmail = trustee.Email,
                    NewExpiresAt = newExpiresAt,
                    SentAt = DateTimeOffset.UtcNow,
                });
        }
    }
}