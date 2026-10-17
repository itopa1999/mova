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

namespace Mova.Application.BBL.Commands.AccountWalletTrustee;

public sealed class DeleteTrusteeCommand
{
    public sealed class Command : IRequest<BaseResult<DeleteTrusteeResponseDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long TrusteeId { get; set; }
    }

    public sealed class DeleteTrusteeResponseDto
    {
        public bool Deleted { get; init; }
        public IReadOnlyList<LinkedWalletDto>? LinkedWallets { get; init; }
        public int? ReleaseDecisionCount { get; init; }
    }

    public sealed class LinkedWalletDto
    {
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<DeleteTrusteeResponseDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IIdentityService _identityService;
        private readonly INotificationQueue _notificationQueue;
        private readonly IPinChallenge _pinChallenge;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IIdentityService identityService,
            INotificationQueue notificationQueue,
            IPinChallenge pinChallenge,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _identityService = identityService;
            _notificationQueue = notificationQueue;
            _pinChallenge = pinChallenge;
            _logger = logger;
        }

        public async Task<BaseResult<DeleteTrusteeResponseDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "DeleteTrustee",
                ("UserId", request.UserPublicId),
                ("TrusteeId", request.TrusteeId));

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.DeleteTrustee,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<DeleteTrusteeResponseDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            var trustee = await _unitOfWork.Query<WalletTrustee>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.TrusteeId &&
                         x.OwnerPublicId == request.UserPublicId,
                    cancellationToken);

            if (trustee is null)
            {
                op.Warning("Trustee not found for owner.");
                return new BaseResult<DeleteTrusteeResponseDto>(
                    HttpStatusCode.NotFound,
                    "Trustee not found.");
            }

            var linkedWallets = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .Where(w =>
                    w.WalletTrusteeId == trustee.Id &&
                    w.Type == WalletType.Trustee &&
                    w.Status == WalletStatus.Active)
                .Select(w => new LinkedWalletDto { Id = w.Id, Name = w.Name })
                .ToListAsync(cancellationToken);

            if (linkedWallets.Count > 0)
            {
                op.Warning(
                    $"Cannot delete trustee — used by {linkedWallets.Count} active wallet(s).");
                return new BaseResult<DeleteTrusteeResponseDto>(
                    HttpStatusCode.Conflict,
                    $"This trustee is used by {linkedWallets.Count} active wallet(s). " +
                    $"Wallets must be completed before deleting.",
                    new DeleteTrusteeResponseDto
                    {
                        Deleted = false,
                        LinkedWallets = linkedWallets,
                    });
            }

            var releaseDecisionCount = await _unitOfWork.Query<ReleaseRequest>()
                .AsNoTracking()
                .CountAsync(
                    x => x.DecidedByTrusteeId == trustee.Id,
                    cancellationToken);

            if (releaseDecisionCount > 0)
            {
                op.Warning(
                    $"Cannot delete trustee — has {releaseDecisionCount} past release decision(s).");
                return new BaseResult<DeleteTrusteeResponseDto>(
                    HttpStatusCode.Conflict,
                    $"This trustee has approved or rejected {releaseDecisionCount} " +
                    $"release request(s). Their decision history must be preserved, " +
                    $"so the trustee cannot be deleted.",
                    new DeleteTrusteeResponseDto
                    {
                        Deleted = false,
                        ReleaseDecisionCount = releaseDecisionCount,
                    });
            }

            var wasVerified = trustee.Status == WalletTrusteeStatus.Verified;
            var trusteeEmail = trustee.Email;
            var trusteeName = trustee.Name;
            var trusteeDisplay = string.IsNullOrWhiteSpace(trusteeName)
                ? trusteeEmail
                : trusteeName;

            var rowsAffected = await _unitOfWork.Query<WalletTrustee>()
                .Where(x => x.Id == trustee.Id)
                .ExecuteDeleteAsync(cancellationToken);

            if (rowsAffected == 0)
            {
                op.Warning("Race lost — trustee already deleted.");
                return new BaseResult<DeleteTrusteeResponseDto>(
                    HttpStatusCode.Gone,
                    "This trustee has already been removed.");
            }

            op.Success(
                $"Trustee deleted. TrusteeId: {trustee.Id}, " +
                $"WasVerified: {wasVerified}");

            if (wasVerified)
            {
                var ownerName = string.IsNullOrWhiteSpace(request.FirstName)
                    ? "The wallet owner"
                    : request.FirstName;

                _notificationQueue.QueueNotificationEmail(
                    trusteeDisplay,
                    trusteeEmail,
                    $"{ownerName} has removed you as their trustee on MOVA.\n\n" +
                    $"You no longer have any trustee duties for their wallets. " +
                    $"If this was unexpected, please reach out to them directly.",
                    "You've been removed as a trustee on MOVA");
            }

            return new BaseResult<DeleteTrusteeResponseDto>(
                HttpStatusCode.OK,
                wasVerified
                    ? "Trustee removed. They have been notified by email."
                    : "Invitation withdrawn.",
                new DeleteTrusteeResponseDto { Deleted = true });
        }
    }
}