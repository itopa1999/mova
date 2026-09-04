using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mova.Api.Configurations;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.BBL.Queries.SchedulePreview;
using Mova.Shared.Common;
using static Mova.Application.BBL.Queries.SchedulePreview.SchedulePreviewQuery;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/wallet")]
[ApiExplorerSettings(GroupName = "v1")]
public class WalletController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    [HttpPost("create")]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> CreateWallet([FromBody] CreateWalletCommand.Command command, CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("preview")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(BaseResult<SchedulePreviewResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> PreviewSchedule(
        [FromBody] SchedulePreviewQuery.Query query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }
}