using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWalletTrustee;

public sealed class InviteTrusteeCommand
{
    public sealed class Command : IRequest<BaseResult<InviteTrusteeResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(320)]
        public string TrusteeEmail { get; set; } = string.Empty;

        [MaxLength(150)]
        public string? TrusteeName { get; set; }
    }

    public sealed class InviteTrusteeResponseDto
    {
        public long TrusteeId { get; init; }
        public string TrusteeEmail { get; init; } = string.Empty;
        public string? TrusteeName { get; init; }
        public DateTimeOffset InvitationExpiresAt { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<InviteTrusteeResponseDto>>
    {
        private readonly IIdentityService _identityService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly INotificationQueue _notificationQueue;
        private readonly IPinChallenge _pinChallenge;
        private readonly TrusteeOptions _trusteeOptions;
        private readonly ICacheService _cacheService;

        public Handler(
            IIdentityService identityService,
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge,
            IOptions<TrusteeOptions> trusteeOptions,
            ICacheService cacheService)
        {
            _identityService = identityService;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
            _trusteeOptions = trusteeOptions.Value;
            _cacheService = cacheService;
        }

        public async Task<BaseResult<InviteTrusteeResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "InviteTrustee",
                ("UserId", request.UserPublicId));

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.CreateTrustee,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<InviteTrusteeResponseDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            var trusteeEmail = (request.TrusteeEmail ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(trusteeEmail))
            {
                op.Warning("Trustee email is required.");
                return new BaseResult<InviteTrusteeResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Please provide the email of the person you trust.");
            }

            var ownerEmail = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(ownerEmail) && trusteeEmail == ownerEmail)
            {
                op.Warning("Owner attempted to invite themselves as trustee.");
                return new BaseResult<InviteTrusteeResponseDto>(
                    HttpStatusCode.BadRequest,
                    "You cannot be your own trustee. Please enter someone else's email.");
            }

            var trusteeName = string.IsNullOrWhiteSpace(request.TrusteeName)
                ? null
                : request.TrusteeName.Trim();

            if (trusteeName is { Length: > 150 })
            {
                op.Warning("Trustee name is too long.");
                return new BaseResult<InviteTrusteeResponseDto>(
                    HttpStatusCode.BadRequest,
                    "Trustee name cannot exceed 150 characters.");
            }

            var existingTrustee = await _unitOfWork.Query<WalletTrustee>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.OwnerPublicId == request.UserPublicId &&
                         x.Email == trusteeEmail,
                    cancellationToken);

            if (existingTrustee is not null)
            {
                if (existingTrustee.Status == WalletTrusteeStatus.Verified)
                {
                    op.Warning(
                        $"Trustee already verified for {trusteeEmail}. " +
                        $"TrusteeId: {existingTrustee.Id}");
                    return new BaseResult<InviteTrusteeResponseDto>(
                        HttpStatusCode.Conflict,
                        "You already have this person as a verified trustee. " +
                        "You can select them when creating a trustee wallet.");
                }

                op.Warning(
                    $"Invitation already pending for {trusteeEmail}. " +
                    $"TrusteeId: {existingTrustee.Id}");
                return new BaseResult<InviteTrusteeResponseDto>(
                    HttpStatusCode.Conflict,
                    "An invitation is already pending for this email. " +
                    "Use the resend endpoint if you need to send it again.");
            }

            var rawToken = TrusteeTokenHelper.Generate();
            var tokenHash = TrusteeTokenHelper.Hash(rawToken);
            var expiresAt = DateTimeOffset.UtcNow.AddHours(_trusteeOptions.InvitationExpiryHours);

            var trustee = new WalletTrustee
            {
                OwnerPublicId = request.UserPublicId,
                Email = trusteeEmail,
                Name = trusteeName,
                Status = WalletTrusteeStatus.Pending,
                VerificationTokenHash = tokenHash,
                TokenExpiresAt = expiresAt,
                InvitedAt = DateTimeOffset.UtcNow,
            };

            await _unitOfWork.AddAsync(trustee, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _cacheService.DeleteAsync(
                CacheKeys.OwnerTrustees(request.UserPublicId),
                cancellationToken);

            var consentUrl = $"{_trusteeOptions.ConsentBaseUrl.TrimEnd('/')}/{rawToken}";
            var trusteeDisplayName = trusteeName ?? trusteeEmail.Split('@')[0];
            var ownerName = string.IsNullOrWhiteSpace(request.FirstName)
                ? "The wallet owner"
                : request.FirstName;

            _notificationQueue.QueueNotificationEmail(
                trusteeDisplayName,
                trusteeEmail,
                $"{ownerName} has invited you to be their trustee on MOVA.\n\n" +
                $"As a trustee, you will be asked to approve or reject money releases " +
                $"from wallets they assign to you. No money moves without your approval.\n\n" +
                $"To accept or decline this invitation, open the link below:\n{consentUrl}\n\n" +
                $"This link expires in {_trusteeOptions.InvitationExpiryHours} hours. If you " +
                $"don't recognise this invitation or don't want to be a trustee, you can " +
                $"ignore this email — the invitation will expire automatically.",
                $"{ownerName} invited you to be a trustee on MOVA");

            op.Success(
                $"Trustee invited. TrusteeId: {trustee.Id}, Email: {trusteeEmail}, " +
                $"ExpiresAt: {expiresAt:u}");

            return new BaseResult<InviteTrusteeResponseDto>(
                HttpStatusCode.Created,
                $"Invitation sent. Your trustee will need to accept it within " +
                $"{_trusteeOptions.InvitationExpiryHours} hours.",
                new InviteTrusteeResponseDto
                {
                    TrusteeId = trustee.Id,
                    TrusteeEmail = trustee.Email,
                    TrusteeName = trustee.Name,
                    InvitationExpiresAt = expiresAt,
                });
        }
    }
}