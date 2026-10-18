using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class ToggleWalletStatusCommand
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long WalletId { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<object>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly INotificationQueue _notificationQueue;
        private readonly IIdentityService _identityService;
        private readonly IPinChallenge _pinChallenge;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            INotificationQueue notificationQueue,
            IIdentityService identityService,
            IPinChallenge pinChallenge)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _notificationQueue = notificationQueue;
            _identityService = identityService;
            _pinChallenge = pinChallenge;
        }

        public async Task<BaseResult<object>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "ToggleWalletStatus",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Error($"Permissions unavailable for user {request.UserPublicId}.");
                return new BaseResult<object>(
                    HttpStatusCode.InternalServerError,
                    "We couldn't verify your account permissions right now. Please try again in a moment.");
            }

            if (!permissions.CanPerformSensitiveOperations)
            {
                op.Warning(
                    $"Sensitive-operation gate blocked ToggleWalletStatus for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, " +
                    $"Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from performing this operation. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<object>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.WalletId
                         && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Warning($"Wallet not found: {request.WalletId}");
                return new BaseResult<object>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.Status is not (WalletStatus.Active or WalletStatus.Paused))
            {
                var rejection = wallet.Status switch
                {
                    WalletStatus.Broken => "A broken wallet cannot be paused or resumed.",
                    WalletStatus.Completed => "A completed wallet cannot be paused or resumed.",
                    WalletStatus.Closed => "A closed wallet cannot be paused or resumed.",
                    _ => "This wallet cannot be toggled.",
                };

                op.Warning($"Wallet cannot be toggled. Status: {wallet.Status}");
                return new BaseResult<object>(HttpStatusCode.BadRequest, rejection);
            }

            var isPausing = wallet.Status == WalletStatus.Active;

            var challengeType = isPausing
                ? PinChallengeType.PausedWallet
                : PinChallengeType.UnpausedWallet;

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                challengeType,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning(
                    $"PIN challenge validation failed for {challengeType}: {challengeMessage}");

                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            wallet.Status = isPausing
                ? WalletStatus.Paused
                : WalletStatus.Active;

            var targetReleaseStatus = isPausing
                ? ReleaseStatus.Scheduled
                : ReleaseStatus.Paused;

            var newReleaseStatus = isPausing
                ? ReleaseStatus.Paused
                : ReleaseStatus.Scheduled;

            var releasesToUpdate = await _unitOfWork.Query<ScheduledRelease>()
                .Where(x => x.WalletId == wallet.Id
                            && x.Status == targetReleaseStatus)
                .ToListAsync(cancellationToken);

            foreach (var release in releasesToUpdate)
            {
                release.Status = newReleaseStatus;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var affectedReleases = releasesToUpdate.Count;

            op.Success(
                isPausing
                    ? $"Wallet paused. {affectedReleases} release(s) on hold."
                    : $"Wallet resumed. {affectedReleases} release(s) rescheduled.");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                isPausing
                    ? $"{wallet.Name} wallet paused"
                    : $"{wallet.Name} wallet resumed",
                isPausing
                    ? "All upcoming releases for this wallet are now on hold. Your funds remain safe and you can resume anytime."
                    : "Your wallet is active again. All scheduled releases have been rescheduled and will resume as normal.",
                $"/dashboard/scheduled/wallet/{request.WalletId}",
                null,
                CancellationToken.None);

            return new BaseResult<object>(
                HttpStatusCode.OK,
                isPausing
                    ? $"Wallet paused successfully. {affectedReleases} release(s) on hold."
                    : $"Wallet resumed successfully. {affectedReleases} release(s) back on schedule.",
                new
                {
                    notification = true,
                    status = isPausing ? "Paused" : "Active",
                    affectedReleases,
                });
        }
    }
}