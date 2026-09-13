using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.MovaAPIs;
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
        private readonly IMediator _mediator;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IMediator mediator,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _mediator = mediator;
            _logger = logger;
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

            var isPausing = wallet.Status == WalletStatus.Active;

            wallet.Status = isPausing
                ? WalletStatus.Paused
                : WalletStatus.Active;

            // 4. Flip all relevant scheduled releases
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

            // 5. Save
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            op.Success(
                isPausing
                    ? $"Wallet paused. {releasesToUpdate.Count} release(s) on hold."
                    : $"Wallet resumed. {releasesToUpdate.Count} release(s) rescheduled.");

            // 6. Notify the user
            try
            {
                await _mediator.Send(
                    new CreateNotificationCommand.Command
                    {
                        UserPublicId = request.UserPublicId,
                        Type = NotificationType.Wallet,
                        Title = isPausing
                            ? $"{wallet.Name} wallet paused"
                            : $"{wallet.Name} wallet resumed",
                        Message = isPausing
                            ? "All upcoming releases for this wallet are now on hold. Your funds remain safe and you can resume anytime."
                            : "Your wallet is active again. All scheduled releases have been rescheduled and will resume as normal.",
                        ActionUrl = $"/wallet/{wallet.Id}",
                    },
                    cancellationToken);
            }
            catch (Exception notifEx)
            {
                op.Fail("Failed to send wallet-toggle notification.", notifEx);
                // swallow — the toggle itself succeeded
            }

            return new BaseResult<object>(
                HttpStatusCode.OK,
                isPausing
                    ? $"Wallet paused successfully. {releasesToUpdate.Count} release(s) on hold."
                    : $"Wallet resumed successfully. {releasesToUpdate.Count} release(s) back on schedule.",
                new
                {
                    notification = true,
                    status = wallet.Status.ToString(),
                    affectedReleases = releasesToUpdate.Count,
                });
        }
    }
}