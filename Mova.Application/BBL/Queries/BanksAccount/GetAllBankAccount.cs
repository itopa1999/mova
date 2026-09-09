using System.Net;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;

namespace Mova.Application.BBL.Queries.BanksAccount;

public sealed class GetAllBankAccount
{
    public class Query : IRequest<BaseResult<List<GetAllBankAccountDto>>>
    {
        [JsonIgnore]
        public string UserPublicId { get; set; } = string.Empty;
    }

    public class GetAllBankAccountDto
    {
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<List<GetAllBankAccountDto>>>
    {
        private readonly IUnitOfWork _unitOfWork;

        public Handler(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<BaseResult<List<GetAllBankAccountDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserPublicId))
            {
                return new BaseResult<List<GetAllBankAccountDto>>(
                    HttpStatusCode.BadRequest,
                    "User public ID is required.");
            }

            var accounts = await _unitOfWork.Query<BankAccount>()
                .AsNoTracking()
                .Where(x => x.UserPublicId == request.UserPublicId && x.Status == BankAccountStatus.Active)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new GetAllBankAccountDto
                {
                    AccountNumber = x.AccountNumber,
                    AccountName = x.AccountName,
                    BankName = x.BankName,
                    IsDefault = x.IsDefault
                })
                .ToListAsync(cancellationToken);

            return new BaseResult<List<GetAllBankAccountDto>>(HttpStatusCode.OK, "Banks Account retrieve successfully", accounts);
        }
    }
}