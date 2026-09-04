using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mova.Api.Configurations;
using Mova.Application.BBL.Commands.TransactionPin;
using Mova.Shared.Common;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/security/pin")]
[ApiExplorerSettings(GroupName = "v1")]
public class TransactionPinController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    [HttpPost("set")]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> SetPin( [FromBody] SetPinCommand.Command command, CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("verify")]
    public async Task<IActionResult> VerifyPin(
        [FromBody] VerifyPinCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPut("change")]
    public async Task<IActionResult> ChangePin(
        [FromBody] ChangePinCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);

    }
}