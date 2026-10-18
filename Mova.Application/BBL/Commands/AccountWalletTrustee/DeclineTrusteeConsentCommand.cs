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

public sealed class DeclineTrusteeConsentCommand
{
    public sealed class Command : IRequest<BaseResult>
    {
        [Required, MinLength(20), MaxLength(100)]
        public string Token { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Reason { get; set; }
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
            using var op = OperationLogger.Start(_logger, "DeclineTrusteeConsent");

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

            if (trustee.Status != WalletTrusteeStatus.Pending)
            {
                op.Warning(
                    $"Cannot decline — trustee already accepted. TrusteeId: {trustee.Id}");
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This invitation has already been accepted and cannot be declined.");
            }

            if (trustee.TokenExpiresAt is null ||
                trustee.TokenExpiresAt <= DateTimeOffset.UtcNow)
            {
                op.Warning($"Token expired at {trustee.TokenExpiresAt:u}");
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This invitation has expired. Ask the wallet owner to send a new one.");
            }

            var rowsAffected = await _unitOfWork.Query<WalletTrustee>()
                .Where(x =>
                    x.Id == trustee.Id &&
                    x.Status == WalletTrusteeStatus.Pending)
                .ExecuteDeleteAsync(cancellationToken);

            if (rowsAffected == 0)
            {
                op.Warning("Race lost — row already gone or accepted.");
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This invitation is no longer available.");
            }

            var trusteeDisplay = string.IsNullOrWhiteSpace(trustee.Name)
                ? trustee.Email
                : trustee.Name;

            var reasonClause = string.IsNullOrWhiteSpace(request.Reason)
                ? string.Empty
                : $" They said: \"{request.Reason.Trim()}\"";

            op.Success(
                $"Trustee declined and row deleted. TrusteeId: {trustee.Id}, " +
                $"OwnerId: {trustee.OwnerPublicId}");

            _notificationQueue.InAppNotificationAsync(
                trustee.OwnerPublicId,
                NotificationType.Wallet,
                "Trustee invitation declined",
                $"{trusteeDisplay} declined to be your trustee.{reasonClause} " +
                $"You can invite someone else whenever you're ready.",
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
                    $"{trusteeDisplay} declined to be your trustee.{reasonClause}\n\n" +
                    $"You can invite someone else from your Trustees page whenever you're ready.",
                    "Trustee invitation declined");
            }

            return new BaseResult(
                HttpStatusCode.OK,
                "Invitation declined. The wallet owner will be notified.");
        }
    }
}