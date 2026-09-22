using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
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

        public long BankAccountId { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<object>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _notificationQueue = notificationQueue;
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

            long bankAccountId = 0;
            string accountName = string.Empty;
            string bankName = string.Empty;

            try
            {
                var account = await _unitOfWork.Query<BankAccount>()
                    .FirstOrDefaultAsync(
                        x =>
                            x.Id == request.BankAccountId &&
                            x.UserPublicId == request.UserPublicId,
                        cancellationToken);

                if (account is null)
                {
                    op.Fail($"Bank account not found: {request.BankAccountId}");

                    return new BaseResult<object>(
                        HttpStatusCode.NotFound,
                        "Bank account not found.");
                }

                // ─── Guard: is this bank account used by a live wallet? ───
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
                    op.Fail(
                        $"Bank account still linked to wallet: {walletInUse.Id} " +
                        $"({walletInUse.Status})");

                    return new BaseResult<object>(
                        HttpStatusCode.Conflict,
                        $"This bank account is linked to the active wallet " +
                        $"\"{walletInUse.Name}\". Please change the wallet's payout " +
                        $"destination or close the wallet first.");
                }

                account.IsDeleted = true;

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                bankAccountId = account.Id;
                accountName = account.AccountName;
                bankName = account.BankName;

                op.Success(
                    $"Bank account deleted successfully. " +
                    $"Id: {bankAccountId}, Bank: {bankName}");
            }
            catch (Exception ex)
            {
                op.Fail(
                    $"Error deleting bank account. BankAccountId: {request.BankAccountId}",
                    ex);

                throw;
            }

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    request.UserPublicId,
                    NotificationType.System,
                    "Bank account removed",
                    $"{accountName} ({bankName}) has been removed from your MOVA account.",
                    "/bank",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for deleted bank account {BankAccountId}.",
                    bankAccountId);
            }

            return new BaseResult<object>(
                HttpStatusCode.OK,
                "Bank account deleted successfully.",
                new
                {
                    notification = true,
                });
        }
    }
}