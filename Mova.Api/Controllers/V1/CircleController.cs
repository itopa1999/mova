using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.SavingsCircles;
using Mova.Application.BBL.Queries.SavingsCircles;
using Mova.Shared.Constants;
using static Mova.Application.BBL.Commands.SavingsCircles.CreateCircleCommand;
using static Mova.Application.BBL.Queries.SavingsCircles.SearchCircleInviteeQuery;
using Mova.Shared.Common.Results;
using Asp.Versioning;
using Mova.Api.Filters;

namespace Mova.Api.Controllers.V1;

/// <summary>
/// Savings-circle endpoints: create, invite/accept, update rotation/status,
/// and read details, rounds, members, and contributions.
/// </summary>
/// <remarks>
/// Rotation, status, and member-removal use <see cref="RateLimitPolicies.Sensitive"/>;
/// other mutations use <see cref="RateLimitPolicies.Write"/>; reads use
/// <see cref="RateLimitPolicies.Read"/>.
/// </remarks>
[ApiController]
[Authorize(Roles = Roles.Customer)]
[Route("api/v{version:apiVersion}/circle")]
[ApiVersion("1.0")]
[RestrictPlatform(Platforms.Web, Platforms.Swagger)]
public class CircleController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    // ----------------------- Commands -----------------------

    /// <summary>
    /// Creates a new savings circle owned by the authenticated customer.
    /// </summary>
    [HttpPost("create")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<CreateCircleResponseDto>), (int)HttpStatusCode.Created)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> CreateCircle(
        [FromBody] CreateCircleCommand.Command command,
        CancellationToken cancellationToken)
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

    /// <summary>
    /// Accepts a pending circle invitation for the authenticated customer
    /// and enrolls them in the selected contribution category.
    /// </summary>
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

    public sealed class AcceptCircleInvitationRequest
    {
        public long CategoryId { get; set; }
    }

    /// <summary>
    /// Searches for users that can be invited to a circle, based on the
    /// supplied search criteria.
    /// </summary>
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

    /// <summary>
    /// Updates the payout rotation order for a circle. Because the rotation
    /// determines which member receives the pooled payout on each round,
    /// this endpoint is treated as money-sensitive and throttled with
    /// <see cref="RateLimitPolicies.Sensitive"/>.
    /// </summary>
    [HttpPatch("details/{walletId:long}/rotation")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<UpdateCircleRotationResponse>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Forbidden)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> UpdateCircleRotation(
        [FromRoute] long walletId,
        [FromBody] UpdateRotationBody body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCircleRotationCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            WalletId = walletId,
            Positions = body.Positions
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    public sealed class UpdateRotationBody
    {
        public List<RotationPositionItem> Positions { get; set; } = new();
    }

    /// <summary>
    /// Removes a member from a circle. Because it changes the financial
    /// membership of the pool, this endpoint is throttled with
    /// <see cref="RateLimitPolicies.Sensitive"/>.
    /// </summary>
    [HttpDelete("details/{walletId:long}/members/{userPublicId}")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<RemoveCircleMemberResponse>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Forbidden)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> RemoveCircleMember(
        [FromRoute] long walletId,
        [FromRoute] string userPublicId,
        CancellationToken cancellationToken)
    {
        var command = new RemoveCircleMemberCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            WalletId = walletId,
            TargetUserPublicId = userPublicId
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Updates the descriptive details of a circle (name, description,
    /// category, schedule, etc.). No money is moved.
    /// </summary>
    [HttpPut("details/{walletId:long}")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<UpdateCircleDetailsResponse>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Forbidden)]
    public async Task<IActionResult> UpdateCircleDetails(
        [FromRoute] long walletId,
        [FromBody] UpdateCircleDetailsCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.WalletId = walletId;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Changes the lifecycle status of a circle (e.g. pause, resume,
    /// complete, cancel). Because the transition may affect an active
    /// financial pool, this endpoint is throttled with
    /// <see cref="RateLimitPolicies.Sensitive"/>.
    /// </summary>
    [HttpPatch("details/{walletId:long}/status")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<UpdateCircleStatusResponse>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Forbidden)]
    public async Task<IActionResult> UpdateCircleStatus(
        [FromRoute] long walletId,
        [FromBody] UpdateCircleStatusBody body,
        CancellationToken cancellationToken)
    {
        var command = new UpdateCircleStatusCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            WalletId = walletId,
            TargetStatus = body.Status
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    public sealed class UpdateCircleStatusBody
    {
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Sends invitations to one or more users to join a circle.
    /// </summary>
    [HttpPost("{circleId:long}/invites")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<InviteCircleMembersCommand.InviteMembersResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> InviteCircleMembers(
        [FromRoute] long circleId,
        [FromBody] List<InviteCircleMembersCommand.InviteMemberDto> body,
        CancellationToken cancellationToken)
    {
        var command = new InviteCircleMembersCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            CircleId = circleId,
            Invites = body
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    // ----------------------- Queries -----------------------

    /// <summary>
    /// Returns the public consent / preview information for a circle
    /// associated with the given invite code.
    /// </summary>
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

    /// <summary>
    /// Returns the details (configuration, current round, balances,
    /// schedule) of a circle the caller is a member of.
    /// </summary>
    [HttpGet("details/{walletId:long}")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<CircleDetailsResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Forbidden)]
    public async Task<IActionResult> GetCircleDetails(
        [FromRoute] long walletId,
        CancellationToken cancellationToken)
    {
        var query = new GetCircleDetailsQuery.Query
        {
            UserPublicId = UserPublicId ?? string.Empty,
            WalletId = walletId
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Returns the list of rounds (payout cycles) for a circle the caller
    /// is a member of.
    /// </summary>
    [HttpGet("details/{walletId:long}/rounds")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<CircleRoundListResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Forbidden)]
    public async Task<IActionResult> GetCircleRounds(
        [FromRoute] long walletId,
        CancellationToken cancellationToken)
    {
        var query = new GetCircleRoundsQuery.Query
        {
            UserPublicId = UserPublicId ?? string.Empty,
            WalletId = walletId
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Returns the members of a circle the caller is a member of,
    /// optionally filtered by membership status.
    /// </summary>
    [HttpGet("details/{walletId:long}/members")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<CircleMembersResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Forbidden)]
    public async Task<IActionResult> GetCircleMembers(
        [FromRoute] long walletId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var query = new GetCircleMembersQuery.Query
        {
            UserPublicId = UserPublicId ?? string.Empty,
            WalletId = walletId,
            StatusFilter = status
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Returns the contributions made in a circle, optionally narrowed to
    /// a specific member.
    /// </summary>
    [HttpGet("details/{walletId:long}/contributions")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetCircleContributionsResponse>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Forbidden)]
    public async Task<IActionResult> GetCircleContributions(
        [FromRoute] long walletId,
        [FromQuery] string? targetMemberUserPublicId,
        CancellationToken cancellationToken)
    {
        var query = new GetCircleContributionsQuery.Query
        {
            UserPublicId = UserPublicId ?? string.Empty,
            WalletId = walletId,
            TargetMemberUserPublicId = targetMemberUserPublicId
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }
}