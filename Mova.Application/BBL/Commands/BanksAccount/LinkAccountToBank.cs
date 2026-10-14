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

public sealed class LinkAccountToBank
{
    public sealed class Command : IRequest<BaseResult<LinkAccountToBankDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        [JsonIgnore]
        public long WalletId { get; set; } = 0;

        [Range(1, long.MaxValue)]
        public long BankAccountId { get; set; }
    }

    public sealed class LinkAccountToBankDto
    {
        public long Id { get; init; }
        public string AccountName { get; init; } = string.Empty;
        public string AccountNumber { get; init; } = string.Empty;
        public string BankName { get; init; } = string.Empty;
        public string BankImageUrl { get; init; } = string.Empty;
        public bool Notification { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<LinkAccountToBankDto>>
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

        public async Task<BaseResult<LinkAccountToBankDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "LinkAccountToBank",
                ("UserId", request.UserPublicId),
                ("WalletId", request.WalletId),
                ("BankAccountId", request.BankAccountId));

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.BadRequest,
                    "User identity is required.");
            }

            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Error($"Permissions unavailable for user {request.UserPublicId}.");
                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.InternalServerError,
                    "We couldn't verify your account permissions right now. Please try again in a moment.");
            }

            if (!permissions.CanPerformSensitiveOperations)
            {
                op.Warning(
                    $"Sensitive-operation gate blocked LinkAccountToBank for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from performing this operation. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

            var wallet = await _unitOfWork.Query<Wallet>()
                .FirstOrDefaultAsync(
                    w => w.Id == request.WalletId
                         && w.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                op.Warning($"Wallet not found: {request.WalletId}");
                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.PayoutDestination == PayoutDestination.Main)
            {
                op.Warning(
                    $"Bank linking not applicable for Main destination. WalletId: {request.WalletId}");
                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.BadRequest,
                    "This wallet sends releases to your main MOVA balance. A bank account can't be linked.");
            }

            var bankAccount = await _unitOfWork.Query<BankAccount>()
                .FirstOrDefaultAsync(
                    b => b.Id == request.BankAccountId
                         && b.UserPublicId == request.UserPublicId
                         && b.ConsentGiven
                         && b.Status == BankAccountStatus.Active
                         && !b.IsDeleted,
                    cancellationToken);

            if (bankAccount is null)
            {
                op.Warning($"Bank account not found: {request.BankAccountId}");
                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.NotFound,
                    "Bank account not found.");
            }

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.LinkBankAccountToWallet,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            await using var tx = await _unitOfWork.BeginAtomicAsync(cancellationToken);

            wallet.BankAccountId = bankAccount.Id;
            _unitOfWork.Update(wallet);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            op.Success(
                $"Bank account linked. WalletId: {wallet.Id}, BankAccountId: {bankAccount.Id}.");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.Wallet,
                "Bank account linked",
                $"{bankAccount.BankName} ({bankAccount.AccountNumber}) has been linked to your {wallet.Name} wallet.",
                $"/wallet/{wallet.Id}",
                null,
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    request.FirstName,
                    request.Email,
                    $"{bankAccount.BankName} ({bankAccount.AccountNumber}) has been linked to your {wallet.Name} wallet. " +
                    "When a release is due, MOVA will send the money to this account automatically.",
                    $"A bank account was linked to your {wallet.Name} wallet");
            }

            return new BaseResult<LinkAccountToBankDto>(
                HttpStatusCode.OK,
                "Bank account linked to wallet successfully.",
                new LinkAccountToBankDto
                {
                    Id = bankAccount.Id,
                    AccountName = bankAccount.AccountName,
                    AccountNumber = bankAccount.AccountNumber,
                    BankName = bankAccount.BankName,
                    BankImageUrl = bankAccount.BankImageUrl,
                    Notification = true,
                });
        }
    }
}