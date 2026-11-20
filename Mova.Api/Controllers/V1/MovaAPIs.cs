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
using Mova.Shared.Common.Results;
using Asp.Versioning;
using Mova.Api.Filters;

namespace Mova.Api.Controllers.V1;

/// <summary>
/// Core Mova endpoints: home dashboard, notifications, and customer feedback.
/// </summary>
/// <remarks>
/// All endpoints require an authenticated customer JWT and use
/// <see cref="RateLimitPolicies.Write"/> for mutations and
/// <see cref="RateLimitPolicies.Read"/> for reads.
/// </remarks>
[ApiController]
[Route("api/v{version:apiVersion}/mova")]
[ApiVersion("1.0")]
[RestrictPlatform(Platforms.Web, Platforms.Swagger)]
public class MovaController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    // ----------------------- Commands -----------------------

    /// <summary>
    /// Marks a single notification as read for the authenticated customer.
    /// </summary>
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

    /// <summary>
    /// Marks all notifications as read for the authenticated customer.
    /// </summary>
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

    /// <summary>
    /// Submits customer feedback on behalf of the authenticated user.
    /// </summary>
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

    // ----------------------- Queries -----------------------

    /// <summary>
    /// Returns the home-dashboard data for the authenticated customer.
    /// </summary>
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

    /// <summary>
    /// Returns the authenticated customer's notifications, optionally only
    /// the unread ones.
    /// </summary>
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
}