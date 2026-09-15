using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.BanksAccount;

public sealed class AddBankAccount
{
    public sealed class Command : IRequest<BaseResult<AddBankAccountDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

        [JsonIgnore]
        public string Email { get; set; } = string.Empty;

        [JsonIgnore]
        public string FirstName { get; set; } = string.Empty;

        public string AccountNumber { get; init; } = string.Empty;

        public string BankCode { get; init; } = string.Empty;

        public bool Consent { get; init; }
    }

    public sealed class AddBankAccountDto
    {
        public long Id { get; set; }

        public string AccountNumber { get; set; } = string.Empty;

        public string AccountName { get; set; } = string.Empty;

        public string BankCode { get; set; } = string.Empty;

        public string BankInstitution { get; set; } = string.Empty;

        public bool IsDefault { get; set; }

        public string Status { get; set; } = string.Empty;

        public bool Notification { get; set; }
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<AddBankAccountDto>>
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPaystackService _paystackService;
        private readonly INotificationQueue _notificationQueue;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IPaystackService paystackService,
            INotificationQueue notificationQueue,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _paystackService = paystackService;
            _notificationQueue = notificationQueue;
            _logger = logger;
        }

        public async Task<BaseResult<AddBankAccountDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(
                _logger,
                "AddBankAccount",
                ("UserId", request.UserPublicId),
                ("BankCode", request.BankCode));

            var accountNumber = request.AccountNumber.Trim();
            var bankCode = request.BankCode.Trim();

            if (string.IsNullOrWhiteSpace(accountNumber))
            {
                op.Fail("Account number is required.");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Account number is required.");
            }

            if (accountNumber.Length != 10 ||
                !accountNumber.All(char.IsDigit))
            {
                op.Fail("Invalid account number format.");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid account number.");
            }

            if (string.IsNullOrWhiteSpace(bankCode))
            {
                op.Fail("Bank code is required.");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Bank code is required.");
            }

            if (!request.Consent)
            {
                op.Fail("Consent not given.");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Consent is required to add this bank account.");
            }

            var bank = await _unitOfWork.Query<Bank>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Code == bankCode && x.IsActive,
                    cancellationToken);

            if (bank is null)
            {
                op.Fail($"Invalid bank code: {bankCode}");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid bank.");
            }

            var existingAccount = await _unitOfWork.Query<BankAccount>()
                .FirstOrDefaultAsync(
                    x =>
                        x.UserPublicId == request.UserPublicId &&
                        x.AccountNumber == accountNumber,
                    cancellationToken);

            if (existingAccount is not null)
            {
                op.Fail($"Bank account already exists: {accountNumber}");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.Conflict,
                    "This bank account has already been added.");
            }

            var verifiedAccount =
                await _paystackService.ResolveBankAccountAsync(
                    accountNumber,
                    bankCode,
                    cancellationToken);

            if (verifiedAccount is null)
            {
                op.Fail($"Unable to verify account: {accountNumber}, bank: {bankCode}");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Unable to verify bank account.");
            }

            var hasDefaultAccount = await _unitOfWork.Query<BankAccount>()
                .AnyAsync(
                    x =>
                        x.UserPublicId == request.UserPublicId &&
                        x.IsDefault &&
                        x.Status == BankAccountStatus.Active,
                    cancellationToken);

            long bankAccountId = 0;
            string accountName = string.Empty;
            string bankName = string.Empty;
            bool isDefault = false;
            string status = string.Empty;

            try
            {
                var bankAccount = new BankAccount
                {
                    UserPublicId = request.UserPublicId,
                    AccountNumber = verifiedAccount.AccountNumber,
                    AccountName = verifiedAccount.AccountName,
                    BankCode = bank.Code,
                    BankName = bank.Name,
                    BankImageUrl = bank.Logo,
                    Status = BankAccountStatus.Active,
                    IsDefault = !hasDefaultAccount,
                    VerifiedAt = DateTimeOffset.UtcNow,
                    VerificationMessage = "Account verified successfully.",
                    ConsentGiven = true,
                    ConsentGivenAt = DateTimeOffset.UtcNow,
                    ConsentVersion = "v1",
                    Currency = "NGN",
                };

                await _unitOfWork.AddAsync(bankAccount, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                bankAccountId = bankAccount.Id;
                accountName = bankAccount.AccountName;
                bankName = bankAccount.BankName;
                isDefault = bankAccount.IsDefault;
                status = bankAccount.Status.ToString();

                op.Success(
                    $"Bank account added successfully. " +
                    $"Id: {bankAccountId}, Bank: {bankName}");
            }
            catch (Exception ex)
            {
                op.Fail(
                    $"Error adding bank account. AccountNumber: {accountNumber}",
                    ex);

                throw;
            }

            try
            {
                await SendBankAccountAddedNotificationsAsync(
                    request.UserPublicId,
                    request.Email,
                    request.FirstName,
                    bankAccountId,
                    accountName,
                    bankName);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Notification block failed for added bank account {BankAccountId}.",
                    bankAccountId);
            }

            return new BaseResult<AddBankAccountDto>(
                HttpStatusCode.Created,
                "Bank account added successfully.",
                new AddBankAccountDto
                {
                    Id = bankAccountId,
                    AccountNumber = verifiedAccount.AccountNumber,
                    AccountName = accountName,
                    BankCode = bank.Code,
                    BankInstitution = bankName,
                    IsDefault = isDefault,
                    Status = status,
                    Notification = true,
                });
        }

        private async Task SendBankAccountAddedNotificationsAsync(
            string userPublicId,
            string email,
            string firstName,
            long bankAccountId,
            string accountName,
            string bankName)
        {
            var title = "Bank account added";

            var inAppMessage =
                $"{accountName} ({bankName}) has been linked to your MOVA account.";

            var emailSubject = "Your bank account has been linked";

            var emailMessage =
                $"{accountName} ({bankName}) has been linked to your MOVA account. " +
                $"You can now use this account to fund your MOVA wallets and receive releases.";

            try
            {
                _notificationQueue.InAppNotificationAsync(
                    userPublicId,
                    NotificationType.System,
                    title,
                    inAppMessage,
                    "/bank",
                    null,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "In-app notification failed for added bank account {BankAccountId}.",
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
                    "Email queue failed for added bank account {BankAccountId}.",
                    bankAccountId);
            }
        }
    }
}