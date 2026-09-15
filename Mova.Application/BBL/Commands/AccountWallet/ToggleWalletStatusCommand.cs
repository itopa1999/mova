using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
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

        public long WalletId { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<object>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<Handler> _logger;
        private readonly INotificationQueue _notificationQueue;

        public Handler(
            IUnitOfWork unitOfWork,
            ILogger<Handler> logger,
            INotificationQueue notificationQueue)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
            _notificationQueue = notificationQueue;
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
                op.Fail("UserPublicId is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "UserPublicId is required.");
            }

            string walletName = string.Empty;
            string notificationTitle = string.Empty;
            string notificationMessage = string.Empty;
            int affectedReleases = 0;
            bool isPausing = false;

            try
            {
                var wallet = await _unitOfWork.Query<Wallet>()
                    .FirstOrDefaultAsync(
                        x => x.Id == request.WalletId
                             && x.UserPublicId == request.UserPublicId,
                        cancellationToken);

                if (wallet is null)
                {
                    op.Fail($"Wallet not found: {request.WalletId}");
                    return new BaseResult<object>(
                        HttpStatusCode.NotFound,
                        "Wallet not found.");
                }

                if (wallet.Status is not (WalletStatus.Active or WalletStatus.Paused))
                {
                    op.Fail($"Wallet cannot be toggled. Status: {wallet.Status}");
                    return new BaseResult<object>(
                        HttpStatusCode.BadRequest,
                        wallet.Status switch
                        {
                            WalletStatus.Broken => "A broken wallet cannot be paused or resumed.",
                            WalletStatus.Completed => "A completed wallet cannot be paused or resumed.",
                            WalletStatus.Closed => "A closed wallet cannot be paused or resumed.",
                            _ => "This wallet cannot be toggled.",
                        });
                }

                isPausing = wallet.Status == WalletStatus.Active;

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

                walletName = wallet.Name;
                affectedReleases = releasesToUpdate.Count;

                notificationTitle = isPausing
                    ? $"{walletName} wallet paused"
                    : $"{walletName} wallet resumed";

                notificationMessage = isPausing
                    ? "All upcoming releases for this wallet are now on hold. Your funds remain safe and you can resume anytime."
                    : "Your wallet is active again. All scheduled releases have been rescheduled and will resume as normal.";

                op.Success(
                    isPausing
                        ? $"Wallet paused. {affectedReleases} release(s) on hold."
                        : $"Wallet resumed. {affectedReleases} release(s) rescheduled.");
            }
            catch (Exception ex)
            {
                op.Fail(
                    $"Error toggling wallet status. WalletId: {request.WalletId}",
                    ex);

                throw;
            }

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    request.UserPublicId,
                    NotificationType.Wallet,
                    notificationTitle,
                    notificationMessage,
                    $"/wallet/{request.WalletId}",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for toggled wallet {WalletId}.",
                    request.WalletId);
            }

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