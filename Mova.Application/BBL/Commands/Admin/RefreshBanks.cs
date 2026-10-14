using System.Net;
using MediatR;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Payment;
using Mova.Shared.Common;
using Mova.Shared.Logging;

namespace Mova.Application.BBL.Commands.Admin;

public sealed class RefreshBanks
{
    public sealed class Command : IRequest<BaseResult>
    {
    }

    public sealed class Handler : IRequestHandler<Command, BaseResult>
    {
        private readonly IBankService _bankService;
        private readonly ILogger<Handler> _logger;

        public Handler(
            IBankService bankService,
            ILogger<Handler> logger)
        {
            _bankService = bankService;
            _logger = logger;
        }

        public async Task<BaseResult> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            using var op = OperationLogger.Start(_logger, "RefreshBanks");

            await _bankService.RefreshBanksAsync(cancellationToken);

            op.Success("Banks refreshed successfully.");

            return new BaseResult(
                HttpStatusCode.OK,
                "Bank Refreshed successfully."
            );
        }
    }
}