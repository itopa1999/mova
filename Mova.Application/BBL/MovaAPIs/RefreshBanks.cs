using System.Net;
using MediatR;
using Mova.Application.Interfaces.Payment;
using Mova.Shared.Common;

namespace Mova.Application.BBL.MovaAPIs;

public sealed class RefreshBanks
{
    public sealed class Command : IRequest<BaseResult>
    {
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IBankService _bankService;

        public Handler(IBankService bankService)
        {
            _bankService = bankService;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            await _bankService.RefreshBanksAsync(cancellationToken);

            return new BaseResult(
                HttpStatusCode.OK,
                "Bank Refreshed successfully."
            );
        }
    }
}
