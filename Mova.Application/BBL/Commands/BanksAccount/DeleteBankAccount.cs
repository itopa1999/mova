using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Commands.BanksAccount;

public sealed class DeleteBankAccount
{
    public sealed class Command : IRequest<BaseResult>
    {
        public string UserPublicId { get; set; } = string.Empty;
        public long BankAccountId { get; init; }
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var account = await _unitOfWork.Query<BankAccount>()
                .FirstOrDefaultAsync(
                    x =>
                        x.Id == request.BankAccountId &&
                        x.UserPublicId == request.UserPublicId,
                    cancellationToken);

            if (account is null)
            {
                return new BaseResult(
                    HttpStatusCode.NotFound,
                    "Bank account not found.");
            }

            if (account.IsDefault)
            {
                return new BaseResult(
                    HttpStatusCode.BadRequest,
                    "You cannot delete your default bank account.");
            }

            _unitOfWork.Remove(account);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new BaseResult(
                HttpStatusCode.OK,
                "Bank account deleted successfully.");
        }
    }
}