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

            long walletId = 0;
            string walletName = string.Empty;
            long bankAccountId = 0;
            string accountName = string.Empty;
            string accountNumber = string.Empty;
            string bankName = string.Empty;
            string bankImageUrl = string.Empty;

            try
            {
                var wallet = await _unitOfWork
                    .Query<Wallet>()
                    .FirstOrDefaultAsync(
                        w =>
                            w.Id == request.WalletId &&
                            w.UserPublicId == request.UserPublicId,
                        cancellationToken);

                if (wallet is null)
                {
                    op.Fail($"Wallet not found: {request.WalletId}");

                    return new BaseResult<LinkAccountToBankDto>(
                        HttpStatusCode.NotFound,
                        "Wallet not found.");
                }

                var bankAccount = await _unitOfWork
                    .Query<BankAccount>()
                    .FirstOrDefaultAsync(
                        b =>
                            b.Id == request.BankAccountId &&
                            b.UserPublicId == request.UserPublicId &&
                            b.ConsentGiven &&
                            b.Status == BankAccountStatus.Active,
                        cancellationToken);

                if (bankAccount is null)
                {
                    op.Fail($"Bank account not found: {request.BankAccountId}");

                    return new BaseResult<LinkAccountToBankDto>(
                        HttpStatusCode.NotFound,
                        "Bank account not found.");
                }

                wallet.BankAccountId = bankAccount.Id;

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                walletId = wallet.Id;
                walletName = wallet.Name;
                bankAccountId = bankAccount.Id;
                accountName = bankAccount.AccountName;
                accountNumber = bankAccount.AccountNumber;
                bankName = bankAccount.BankName;
                bankImageUrl = bankAccount.BankImageUrl;

                op.Success(
                    $"Bank account linked to wallet successfully. " +
                    $"WalletId: {walletId}, BankAccountId: {bankAccountId}");
            }
            catch (Exception ex)
            {
                op.Fail(
                    $"Error linking bank account. WalletId: {request.WalletId}, BankAccountId: {request.BankAccountId}",
                    ex);

                throw;
            }

            try
            {
                await SendBankLinkedNotificationsAsync(
                    request.UserPublicId,
                    request.Email,
                    request.FirstName,
                    walletId,
                    walletName,
                    bankAccountId,
                    bankName,
                    accountNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for linked bank account {BankAccountId}.",
                    bankAccountId);
            }

            var response = new LinkAccountToBankDto
            {
                Id = bankAccountId,
                AccountName = accountName,
                AccountNumber = accountNumber,
                BankName = bankName,
                BankImageUrl = bankImageUrl,
                Notification = true,
            };

            return new BaseResult<LinkAccountToBankDto>(
                HttpStatusCode.OK,
                "Bank account linked to wallet successfully.",
                response);
        }

        private async Task SendBankLinkedNotificationsAsync(
            string userPublicId,
            string email,
            string firstName,
            long walletId,
            string walletName,
            long bankAccountId,
            string bankName,
            string accountNumber)
        {
            var title = "Bank account linked";

            var inAppMessage =
                $"{bankName} ({accountNumber}) has been linked to your {walletName} wallet.";

            var emailSubject =
                $"A bank account was linked to your {walletName} wallet";

            var emailMessage =
                $"{bankName} ({accountNumber}) has been linked to your {walletName} wallet. " +
                $"When a release is due, MOVA will send the money to this account automatically.";

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.Wallet,
                    title,
                    inAppMessage,
                    $"/wallet/{walletId}",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for linked bank account {BankAccountId}.",
                    bankAccountId);
            }

            try
            {
                _notificationQueue.QueueNotificationEmail(
                    firstName,
                    email,
                    emailMessage,
                    emailSubject);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Email queue failed for linked bank account {BankAccountId}.",
                    bankAccountId);
            }
        }
    }
}