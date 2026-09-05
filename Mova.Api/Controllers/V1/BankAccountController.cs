using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mova.Api.Configurations;
using Mova.Application.BBL.Commands.BanksAccount;
using Mova.Application.BBL.Queries.BanksAccount;
using Mova.Shared.Common;
using static Mova.Application.BBL.Commands.BanksAccount.AddBankAccount;
using static Mova.Application.BBL.Commands.BanksAccount.VerifyBankAccount;
using static Mova.Application.BBL.Queries.BanksAccount.GetBanks;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/mova")]
[ApiExplorerSettings(GroupName = "v1")]
public class BankAccountController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

        [HttpGet("banks")]
    [ProducesResponseType(typeof(BaseResult<GetBanksDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetBanksDetailsData([FromQuery] string? name, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetBanks.Query
            {
                Name = name
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("banks/refresh")]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> RefreshBanks(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RefreshBanks.Command(),
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("banks/verify")]
    [ProducesResponseType(typeof(BaseResult<VerifyBankAccountDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyBankAccount(
        [FromBody] VerifyBankAccount.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            command,
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("bank-accounts")]
    [ProducesResponseType(typeof(BaseResult<AddBankAccountDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> AddBankAccount(
        [FromBody] AddBankAccount.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId;

        var result = await _mediator.Send(
            command,
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }
}