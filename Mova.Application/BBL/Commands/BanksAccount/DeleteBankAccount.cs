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

namespace Mova.Application.BBL.Commands.BanksAccount;

public sealed class DeleteBankAccount
{
    public sealed class Command : IRequest<BaseResult<object>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [Range(1, long.MaxValue)]
        public long BankAccountId { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<object>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationQueue _notificationQueue;
        private readonly IIdentityService _identityService;
        private readonly IPinChallenge _pinChallenge;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            INotificationQueue notificationQueue,
            IIdentityService identityService,
            IPinChallenge pinChallenge,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _notificationQueue = notificationQueue;
            _identityService = identityService;
            _pinChallenge = pinChallenge;
            _logger = logger;
        }

        public async Task<BaseResult<object>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "DeleteBankAccount",
                ("UserId", request.UserPublicId),
                ("BankAccountId", request.BankAccountId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    "User identity is required.");
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
                    $"Sensitive-operation gate blocked DeleteBankAccount for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from performing this operation. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<object>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.BankAccountDeletion,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<object>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            var account = await _unitOfWork.Query<BankAccount>()
                .FirstOrDefaultAsync(
                    x => x.Id == request.BankAccountId
                         && x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (account is null)
            {
                op.Warning($"Bank account not found: {request.BankAccountId}");
                return new BaseResult<object>(
                    HttpStatusCode.NotFound,
                    "Bank account not found.");
            }

            var walletInUse = await _unitOfWork.Query<Wallet>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.BankAccountId == request.BankAccountId
                         && w.Status != WalletStatus.Broken
                         && w.Status != WalletStatus.Completed
                         && w.Status != WalletStatus.Closed,
                    cancellationToken);

            if (walletInUse is not null)
            {
                op.Warning(
                    $"Bank account still linked to wallet: {walletInUse.Id} ({walletInUse.Status}).");

                return new BaseResult<object>(
                    HttpStatusCode.Conflict,
                    $"This bank account is linked to the active wallet " +
                    $"\"{walletInUse.Name}\". Please change the wallet's payout " +
                    $"destination or close the wallet first.");
            }

            account.IsDeleted = true;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            op.Success(
                $"Bank account deleted. Id: {account.Id}, Bank: {account.BankName}.");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.System,
                "Bank account removed",
                $"{account.AccountName} ({account.BankName}) has been removed from your MOVA account.",
                "/bank",
                null,
                CancellationToken.None);

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "Bank account deleted successfully.",
                new { notification = true });
        }
    }
}