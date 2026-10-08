using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.MovaAPIs;
using Mova.Shared.Common;
using static Mova.Application.BBL.MovaAPIs.GetNotificationsQuery;
using static Mova.Application.BBL.MovaAPIs.HomeQuery;
using static Mova.Application.BBL.MovaAPIs.SubmitFeedbackCommand;
using Mova.Shared.Constants;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Route("api/v1/mova")]
[ApiExplorerSettings(GroupName = "v1")]
public class MovaController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    [HttpGet("home")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Read)]
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
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<List<NotificationDto>>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetAllNotiications(
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetNotificationsQuery.Query
            {
                UserPublicId = UserPublicId ?? string.Empty,
                UnreadOnly = unreadOnly,
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPatch("{id:long}/read-notification")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> MarkAsRead(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new MarkNotificationAsRead.Command
            {
                UserPublicId = UserPublicId ?? string.Empty,
                NotificationId = id,
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }


    [HttpPatch("read-all-notifications")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> MarkAllAsRead(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new MarkAllNotificationsAsRead.Command
            {
                UserPublicId = UserPublicId ?? string.Empty,
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("feedback")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<SubmitFeedbackResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitFeedbackCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.FirstName = UserFirstName ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }
}