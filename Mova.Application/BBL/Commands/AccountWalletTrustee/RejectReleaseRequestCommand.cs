using System.ComponentModel.DataAnnotations;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.AccountWalletTrustee;

public sealed class RejectReleaseRequestCommand
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

        public async Task<BaseResult> Handle(Command request, CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(_logger, "RejectReleaseRequest");

            var tokenHash = TrusteeTokenHelper.Hash(request.Token);
            var now = DateTimeOffset.UtcNow;
            var reason = string.IsNullOrWhiteSpace(request.Reason)
                ? null
                : request.Reason.Trim();

            // Load first to know wallet + amount
            var releaseRequest = await _unitOfWork.Query<ReleaseRequest>()
                .Include(r => r.Wallet)
                .FirstOrDefaultAsync(
                    x => x.ApprovalTokenHash == tokenHash,
                    cancellationToken);

            if (releaseRequest is null)
            {
                return new BaseResult(
                    HttpStatusCode.NotFound,
                    "This link is invalid or has already been used.");
            }

            if (releaseRequest.Status != ReleaseRequestStatus.Pending)
            {
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This request has already been decided.");
            }

            if (releaseRequest.ExpiresAt <= now)
            {
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This request has expired.");
            }

            var wallet = releaseRequest.Wallet;

            if (wallet is null)
            {
                return new BaseResult(
                    HttpStatusCode.InternalServerError,
                    "Wallet not found.");
            }

            // Atomic status flip
            var rowsAffected = await _unitOfWork.Query<ReleaseRequest>()
                .Where(x =>
                    x.Id == releaseRequest.Id &&
                    x.Status == ReleaseRequestStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, ReleaseRequestStatus.Rejected)
                        .SetProperty(x => x.DecidedAt, now)
                        .SetProperty(x => x.DecisionReason, reason)
                        .SetProperty(x => x.ApprovalTokenHash, (byte[]?)null)
                        .SetProperty(x => x.TokenUsedAt, now)
                        .SetProperty(x => x.DecidedByTrusteeId, wallet.WalletTrusteeId),
                    cancellationToken);

            if (rowsAffected == 0)
            {
                return new BaseResult(
                    HttpStatusCode.Gone,
                    "This request has already been decided.");
            }

            // Restore funds
            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            wallet.LockedAmount = Money.FromMinorUnits(
                checked(wallet.LockedAmount.MinorUnits + releaseRequest.Amount.MinorUnits),
                wallet.LockedAmount.Currency);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Release rejected. RequestId: {releaseRequest.Id}, " +
                $"Reason: {reason ?? "(none)"}");

            var reasonClause = string.IsNullOrWhiteSpace(reason)
                ? string.Empty
                : $" They said: \"{reason}\"";

            _notificationQueue.InAppNotificationAsync(
                releaseRequest.RequestedByUserPublicId,
                NotificationType.Wallet,
                "Release rejected",
                $"Your trustee rejected the request to release " +
                $"₦{releaseRequest.Amount.ToDecimal():N0} from \"{wallet.Name}\". " +
                $"Funds are back in the wallet.{reasonClause}",
                $"/dashboard/trustee/wallet/{wallet.Id}",
                null,
                CancellationToken.None);

            var owner = await _identityService.GetByIdentifierAsync(
                releaseRequest.RequestedByUserPublicId,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(owner?.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    owner.FirstName ?? string.Empty,
                    owner.Email,
                    $"Your trustee rejected the release request of " +
                    $"₦{releaseRequest.Amount.ToDecimal():N0} from your " +
                    $"\"{wallet.Name}\" wallet.{reasonClause}\n\n" +
                    $"The funds are back in your wallet.",
                    "Release rejected");
            }

           return new BaseResult<object>(
                HttpStatusCode.OK,
                "Request rejected. The wallet owner has been notified.",
                new { notification = true });
        }
    }
}