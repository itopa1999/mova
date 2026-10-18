using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWalletTrustee;

public sealed class AcceptTrusteeConsentCommand
{
    public sealed class Command : IRequest<BaseResult>
    {
        [Required, MinLength(20), MaxLength(100)]
        public string Token { get; set; } = string.Empty;

        [JsonIgnore]
        public string? IpAddress { get; set; }

        [JsonIgnore]
        public string? UserAgent { get; set; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(_logger, "AcceptTrusteeConsent");

            var tokenHash = TrusteeTokenHelper.Hash(request.Token);

            var trustee = await _unitOfWork.Query<WalletTrustee>()
                .FirstOrDefaultAsync(
                    x => x.VerificationTokenHash == tokenHash,
                    cancellationToken);

            if (trustee is null)
            {
                op.Warning("Token not found.");
                return new BaseResult(
                    HttpStatusCode.NotFound,
                    "This invitation link is invalid or has already been used.");
            }

            if (trustee.Status == WalletTrusteeStatus.Verified)
            {
                op.Warning($"Already verified. TrusteeId: {trustee.Id}");
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This invitation has already been accepted.");
            }

            if (trustee.TokenExpiresAt is null ||
                trustee.TokenExpiresAt <= DateTimeOffset.UtcNow)
            {
                op.Warning($"Token expired at {trustee.TokenExpiresAt:u}");
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This invitation has expired. Ask the wallet owner to send a new one.");
            }

            var now = DateTimeOffset.UtcNow;
            var ipAddress = Truncate(request.IpAddress, 45);

            var rowsAffected = await _unitOfWork.Query<WalletTrustee>()
                .Where(x =>
                    x.Id == trustee.Id &&
                    x.Status == WalletTrusteeStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, WalletTrusteeStatus.Verified)
                        .SetProperty(x => x.VerifiedAt, now)
                        .SetProperty(x => x.VerificationTokenHash, (byte[]?)null)
                        .SetProperty(x => x.TokenExpiresAt, (DateTimeOffset?)null)
                        .SetProperty(x => x.ConsentedFromIp, ipAddress),
                    cancellationToken);

            if (rowsAffected == 0)
            {
                op.Warning("Race lost — someone else already accepted.");
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This invitation has already been accepted.");
            }

            var trusteeDisplay = string.IsNullOrWhiteSpace(trustee.Name)
                ? trustee.Email
                : trustee.Name;

            op.Success(
                $"Trustee verified. TrusteeId: {trustee.Id}, " +
                $"OwnerId: {trustee.OwnerPublicId}, Email: {trustee.Email}");

            _notificationQueue.InAppNotificationAsync(
                trustee.OwnerPublicId,
                NotificationType.Wallet,
                "Trustee accepted your invitation",
                $"{trusteeDisplay} has accepted to be your trustee. " +
                $"You can now select them when creating a trustee wallet. " +
                $"Every release from that wallet will need their approval within 3 hours.",
                "/dashboard/trustees",
                null,
                CancellationToken.None);

            var owner = await _identityService.GetByIdentifierAsync(
                trustee.OwnerPublicId,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(owner?.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    owner.FirstName ?? string.Empty,
                    owner.Email,
                    $"{trusteeDisplay} has accepted to be your trustee on MOVA.\n\n" +
                    $"You can now select them when creating a trustee wallet. Every release " +
                    $"from that wallet will need their approval within a 3-hour window. No " +
                    $"money moves without their approval.",
                    "Your trustee accepted the invitation");
            }

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "You are now a trustee. The wallet owner will be notified.",
                new { notification = true });
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }
}