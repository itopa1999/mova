using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Commands.AccountWallet;

public sealed class LinkAccountToBank
{
    public sealed class Command : IRequest<BaseResult<LinkAccountToBankDto>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;

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
    }

    public sealed class Handler
        : IRequestHandler<Command, BaseResult<LinkAccountToBankDto>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<LinkAccountToBankDto>> Handle(
            Command request,
            CancellationToken cancellationToken)
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
                return new BaseResult<LinkAccountToBankDto>(
                    HttpStatusCode.NotFound,
                    "Bank account not found.");
            }

            wallet.BankAccountId = bankAccount.Id;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var response = new LinkAccountToBankDto
            {
                Id = bankAccount.Id,
                AccountName = bankAccount.AccountName,
                AccountNumber = bankAccount.AccountNumber,
                BankName = bankAccount.BankName,
                BankImageUrl = bankAccount.BankImageUrl
            };

            return new BaseResult<LinkAccountToBankDto>(
                HttpStatusCode.OK,
                "Bank account linked to wallet successfully.",
                response);
        }
    }
}