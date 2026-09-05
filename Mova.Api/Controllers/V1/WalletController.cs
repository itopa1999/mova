using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mova.Api.Configurations;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.BBL.Queries.SchedulePreview;
using Mova.Shared.Common;
using static Mova.Application.BBL.Commands.AccountWallet.AddFundsCommand;
using static Mova.Application.BBL.Queries.AccountWallet.GetWalletSchedulePreviewQuery;
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

    [HttpPost("add-funds")]
    [ProducesResponseType(typeof(BaseResult<AddFundsCommandResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> AddFunds(
        [FromBody] AddFundsCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("{walletId:long}/relock-unused")]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> RelockUnusedFunds(
        long walletId,
        CancellationToken cancellationToken)
    {
        var command = new RelockUnusedFundsCommand.Command
        {
            WalletId = walletId,
            UserPublicId = UserPublicId ?? string.Empty
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpGet("{walletId:long}/schedule-preview")]
    [ProducesResponseType(typeof(BaseResult<GetWalletSchedulePreviewResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetSchedulePreview(long walletId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetWalletSchedulePreviewQuery.Query
            {
                WalletId = walletId,
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
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
