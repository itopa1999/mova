using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Commands.BanksAccount;
public sealed class VerifyBankAccount
{
    public sealed class Command : IRequest<BaseResult<VerifyBankAccountDto>>
    {
        public string AccountNumber { get; init; } = string.Empty;
        public string BankCode { get; init; } = string.Empty;
    }

    public sealed class VerifyBankAccountDto
    {
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string BankInstitution { get; set; } = string.Empty;
        public string BankCode { get; init; } = string.Empty; 
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult<VerifyBankAccountDto>>
    {
        private readonly IPaystackService _paystackService;
        private readonly IUnitOfWork _unitOfWork;

        public Handler(
            IPaystackService paystackService,
            IUnitOfWork unitOfWork)
        {
            _paystackService = paystackService;
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<VerifyBankAccountDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.AccountNumber))
            {
                return new BaseResult<VerifyBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Account number is required.");
            }

            if (string.IsNullOrWhiteSpace(request.BankCode))
            {
                return new BaseResult<VerifyBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Bank code is required.");
            }

            var bankCode = request.BankCode.Trim();
            var accountNumber = request.AccountNumber.Trim();

            var bank = await _unitOfWork.Query<Bank>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Code == bankCode && x.IsActive,
                    cancellationToken);

            if (bank is null)
            {
                return new BaseResult<VerifyBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Invalid bank selected.");
            }

            var account = await _paystackService.ResolveBankAccountAsync(
                accountNumber,
                bankCode,
                cancellationToken);

            if (account is null)
            {
                return new BaseResult<VerifyBankAccountDto>(
                    HttpStatusCode.BadRequest,
                    "Unable to verify bank account.");
            }

            return new BaseResult<VerifyBankAccountDto>(
                HttpStatusCode.OK,
                "Verified successfully",
                new VerifyBankAccountDto
                {
                    AccountNumber = account.AccountNumber,
                    AccountName = account.AccountName,
                    BankInstitution = bank.Name,
                    BankCode = bank.Code
                });
        }
    }
}