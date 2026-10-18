using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
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

        [Required, RegularExpression(@"^\d{10}$", ErrorMessage = "Account number must be exactly 10 digits.")]
        public string AccountNumber { get; init; } = string.Empty;

        [Required, MaxLength(20), RegularExpression(@"^[a-zA-Z0-9]+$")]
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
        private readonly IIdentityService _identityService;
        private readonly IPinChallenge _pinChallenge;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IPaystackService paystackService,
            INotificationQueue notificationQueue,
            IIdentityService identityService,
            IPinChallenge pinChallenge,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _paystackService = paystackService;
            _notificationQueue = notificationQueue;
            _identityService = identityService;
            _pinChallenge = pinChallenge;
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

            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                op.Warning("UserPublicId is required.");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "User identity is required.");
            }

            var permissions = await _identityService.GetPermissionsAsync(
                request.UserPublicId,
                cancellationToken);

            if (permissions is null)
            {
                op.Error($"Permissions unavailable for user {request.UserPublicId}.");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.InternalServerError,
                    "We couldn't verify your account permissions right now. Please try again in a moment.");
            }

            if (!permissions.CanPerformSensitiveOperations)
            {
                op.Warning(
                    $"Sensitive-operation gate blocked AddBankAccount for user {request.UserPublicId}. " +
                    $"Status: {permissions.StatusLabel}, Reason: {permissions.RestrictionReason}");

                var userMessage = string.IsNullOrWhiteSpace(permissions.StatusDescription)
                    ? "Your account is currently restricted from performing this operation. Please contact support."
                    : permissions.StatusDescription;

                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.Locked,
                    userMessage);
            }

            var (challengeValid, challengeMessage) = await _pinChallenge.ValidateAsync(
                request.UserPublicId,
                PinChallengeType.AddBankAccount,
                cancellationToken);

            if (!challengeValid)
            {
                op.Warning($"PIN challenge validation failed: {challengeMessage}");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    challengeMessage);
            }

            var accountNumber = request.AccountNumber.Trim();
            var bankCode = request.BankCode.Trim();

            if (string.IsNullOrWhiteSpace(accountNumber))
            {
                op.Warning("Account number is required.");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Account number is required.");
            }

            if (accountNumber.Length != 10 || !accountNumber.All(char.IsDigit))
            {
                op.Warning("Invalid account number format.");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid account number.");
            }

            if (string.IsNullOrWhiteSpace(bankCode))
            {
                op.Warning("Bank code is required.");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Bank code is required.");
            }

            if (!request.Consent)
            {
                op.Warning("Consent not given.");
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
                op.Warning($"Invalid bank code: {bankCode}");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid bank.");
            }

            var existingAccount = await _unitOfWork.Query<BankAccount>()
                .FirstOrDefaultAsync(
                    x => x.UserPublicId == request.UserPublicId
                         && x.AccountNumber == accountNumber,
                    cancellationToken);

            if (existingAccount is not null)
            {
                op.Warning($"Bank account already exists: {accountNumber}");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.Conflict,
                    "This bank account has already been added.");
            }

            var verifiedAccount = await _paystackService.ResolveBankAccountAsync(
                accountNumber,
                bankCode,
                cancellationToken);

            if (verifiedAccount is null)
            {
                op.Warning($"Unable to verify account: {accountNumber}, bank: {bankCode}");
                return new BaseResult<AddBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Unable to verify bank account.");
            }

            var hasDefaultAccount = await _unitOfWork.Query<BankAccount>()
                .AnyAsync(
                    x => x.UserPublicId == request.UserPublicId
                         && x.IsDefault
                         && x.Status == BankAccountStatus.Active,
                    cancellationToken);

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

            op.Success(
                $"Bank account added. Id: {bankAccount.Id}, Bank: {bankAccount.BankName}.");

            _notificationQueue.InAppNotificationAsync(
                request.UserPublicId,
                NotificationType.System,
                "Bank account added",
                $"{bankAccount.AccountName} ({bankAccount.BankName}) has been linked to your MOVA account.",
                "/dashboard/bank",
                null,
                CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                _notificationQueue.QueueNotificationEmail(
                    request.FirstName,
                    request.Email,
                    $"{bankAccount.AccountName} ({bankAccount.BankName}) has been linked to your MOVA account. " +
                    "You can now use this account to receive releases.",
                    "Your bank account has been linked");
            }

            return new BaseResult<AddBankAccountDto>(
                HttpStatusCode.Created,
                "Bank account added successfully.",
                new AddBankAccountDto
                {
                    Id = bankAccount.Id,
                    AccountNumber = verifiedAccount.AccountNumber,
                    AccountName = bankAccount.AccountName,
                    BankCode = bank.Code,
                    BankInstitution = bankAccount.BankName,
                    IsDefault = bankAccount.IsDefault,
                    Status = bankAccount.Status.ToString(),
                    Notification = true,
                });
        }
    }
}