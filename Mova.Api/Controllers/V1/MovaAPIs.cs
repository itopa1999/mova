using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mova.Api.Configurations;
using Mova.Application.BBL.Commands.FeatureFlags;
using Mova.Application.BBL.MovaAPIs;
using Mova.Shared.Common;
using static Mova.Application.BBL.Commands.FeatureFlags.ToggleFeatureFlag;
using static Mova.Application.BBL.MovaAPIs.GetFeatureFlags;
using static Mova.Application.BBL.MovaAPIs.GetNotificationsQuery;
using static Mova.Application.BBL.MovaAPIs.HomeQuery;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/mova")]
[ApiExplorerSettings(GroupName = "v1")]
public class MovaQueries(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    [HttpGet("home")]
    [ProducesResponseType(typeof(BaseResult<HomeQueryDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetHomeData(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new HomeQuery.Query
            {
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("get-notifications")]
    [ProducesResponseType(typeof(BaseResult<List<NotificationDto>>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetAllNotiications(
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetNotificationsQuery.Query
            {
                UserPublicId = UserPublicId,
                UnreadOnly = unreadOnly,
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPatch("{id:long}/read-notification")]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> MarkAsRead(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new MarkNotificationAsRead.Command
            {
                UserPublicId = UserPublicId,
                NotificationId = id,
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }


    [HttpPatch("read-all-notifications")]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> MarkAllAsRead(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new MarkAllNotificationsAsRead.Command
            {
                UserPublicId = UserPublicId,
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("feature-flags")]
    [ProducesResponseType(typeof(BaseResult<List<FeatureFlagDto>>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetFeatureFlags.Query(), cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("feature-flags/toggle")]
    [ProducesResponseType(typeof(BaseResult<ToggleFeatureFlagDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> Toggle(
        [FromQuery] long id,
        [FromQuery] bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var command = new ToggleFeatureFlag.Command
        {
            Id = id,
            IsEnabled = isEnabled,
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }
}