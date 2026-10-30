using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.SavingsCircles;
using Mova.Application.BBL.Queries.SavingsCircles;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using static Mova.Application.BBL.Commands.SavingsCircles.AcceptCircleInvitationCommand;
using static Mova.Application.BBL.Commands.SavingsCircles.CreateCircleCommand;
using static Mova.Application.BBL.Queries.SavingsCircles.SearchCircleInviteeQuery;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize(Roles = Roles.Customer)]
[Route("api/v1/circle")]
[ApiExplorerSettings(GroupName = "v1")]
public class CircleController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    [HttpPost("create")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<CreateCircleResponseDto>), (int)HttpStatusCode.Created)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> CreateCircle([FromBody] CreateCircleCommand.Command command, CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;
        command.FirstName = UserFirstName ?? string.Empty;
        command.LastName = UserLastName ?? string.Empty;
        command.Phone = UserPhoneNumber ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpGet("invite/{inviteCode}")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<SearchCircleInviteeResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetCircleConsent(
        [FromRoute] string inviteCode,
        CancellationToken cancellationToken)
    {
        var query = new GetCircleConsentQuery.Query
        {
            UserPublicId = UserPublicId ?? string.Empty,
            UserEmail = UserEmail ?? string.Empty,
            InviteCode = inviteCode
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("invite/{inviteCode}/accept")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<AcceptCircleInvitationResponse>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Forbidden)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> AcceptCircleInvitation(
        [FromRoute] string inviteCode,
        [FromBody] AcceptCircleInvitationRequest body,
        CancellationToken cancellationToken)
    {
        var command = new AcceptCircleInvitationCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            InviteCode = inviteCode,
            CategoryId = body.CategoryId
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("invitees/search")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<SearchCircleInviteeResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> SearchCircleInvitee(
        [FromBody] Query query,
        CancellationToken cancellationToken)
    {
        query.UserPublicId = UserPublicId ?? string.Empty;

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }
    
    public sealed class AcceptCircleInvitationRequest
    {
        public long CategoryId { get; set; }
    }
}