using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.BBL.MovaAPIs;
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
        private readonly IMediator _mediator;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IUnitOfWork unitOfWork,
            IPaystackService paystackService,
            IMediator mediator,
            ILogger<Handler> logger)
        {
            _unitOfWork = unitOfWork;
            _paystackService = paystackService;
            _mediator = mediator;
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

            await _mediator.Send(
                new CreateNotificationCommand.Command
                {
                    UserPublicId = request.UserPublicId,
                    Type = NotificationType.System,
                    Title = "Bank account added",
                    Message =
                        $"{verifiedAccount.AccountName} ({bank.Name}) " +
                        $"has been linked to your MOVA account.",
                    ActionUrl = "/bank",
                },
                cancellationToken);

            op.Success(
                $"Bank account added successfully. " +
                $"Id: {bankAccount.Id}, Bank: {bank.Name}");

            return new BaseResult<AddBankAccountDto>(
                HttpStatusCode.Created,
                "Bank account added successfully.",
                new AddBankAccountDto
                {
                    Id = bankAccount.Id,
                    AccountNumber = bankAccount.AccountNumber,
                    AccountName = bankAccount.AccountName,
                    BankCode = bankAccount.BankCode,
                    BankInstitution = bankAccount.BankName,
                    IsDefault = bankAccount.IsDefault,
                    Status = bankAccount.Status.ToString(),
                    Notification = true,
                });
        }
    }
}