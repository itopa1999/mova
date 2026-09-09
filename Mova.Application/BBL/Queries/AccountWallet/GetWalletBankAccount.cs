using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetWalletBankAccount
{
    public sealed class Query : IRequest<BaseResult<BankAccountDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
        public long WalletId { get; set; }
    }

    public sealed class BankAccountDto
    {
        public long Id { get; init; }

        public string AccountName { get; init; } = string.Empty;

        public string AccountNumber { get; init; } = string.Empty;

        public string BankName { get; init; } = string.Empty;

        public string BankImageUrl { get; init; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<BankAccountDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<BankAccountDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var wallet = await _unitOfWork
                .Query<Wallet>()
                .AsNoTracking()
                .Include(w => w.BankAccount)
                .FirstOrDefaultAsync(
                    w =>
                        w.Id == request.WalletId &&
                        w.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (wallet is null)
            {
                return new BaseResult<BankAccountDto>(
                    HttpStatusCode.NotFound,
                    "Wallet not found.");
            }

            if (wallet.BankAccountId is null || wallet.BankAccount is null)
            {
                return new BaseResult<BankAccountDto>(
                    HttpStatusCode.OK,
                    "No bank account is linked to this wallet.",
                    null);
            }

            var bankAccount = wallet.BankAccount;

            var response = new BankAccountDto
            {
                Id = bankAccount.Id,
                AccountName = bankAccount.AccountName,
                AccountNumber = bankAccount.AccountNumber,
                BankName = bankAccount.BankName,
                BankImageUrl = bankAccount.BankImageUrl
            };

            return new BaseResult<BankAccountDto>(
                HttpStatusCode.OK,
                "Wallet bank account retrieved successfully.",
                response);
        }
    }
}