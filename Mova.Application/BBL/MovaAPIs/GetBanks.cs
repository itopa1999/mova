using System.Net;
using MediatR;
using Mova.Application.Interfaces.Payment;
using Mova.Shared.Common;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class GetBanks
{
    public sealed class Query : IRequest<BaseResult<GetBanksDto>>
    {
        public string? Name { get; init; }
    }

    public sealed class GetBanksDto
    {
        public List<BankDto> Banks { get; init; } = [];
    }

    public sealed class Handler : IRequestHandler<Query, BaseResult<GetBanksDto>>
    {
        private readonly IBankService _bankService;

        public Handler(IBankService bankService)
        {
            _bankService = bankService;
        }

        public async Task<BaseResult<GetBanksDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var banks = await _bankService.GetAllBanksAsync(request.Name);

            return new BaseResult<GetBanksDto>(
                HttpStatusCode.OK,
                "Bank data retrieved successfully.",
                new GetBanksDto
                {
                    Banks = banks
                });
        }
    }
}