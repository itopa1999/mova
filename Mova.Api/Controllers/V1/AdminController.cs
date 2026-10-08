using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.Admin;
using Mova.Application.BBL.Queries.Admin;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using static Mova.Application.BBL.Commands.Admin.CreateVirtualAccountForUserCommand;
using static Mova.Application.BBL.Commands.Admin.ToggleFeatureFlag;
using static Mova.Application.BBL.Queries.Admin.GetFeatureFlags;


namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize(Roles = Roles.AllAdmins)]
[Route("api/v1/admin")]
[ApiExplorerSettings(GroupName = "v1")]
public class AdminController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    [HttpGet("feature-flags")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
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
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<ToggleFeatureFlagDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> Toggle(
        [FromQuery] long id,
        [FromQuery, Required] bool? isEnabled,
        CancellationToken cancellationToken = default)
    {
        var command = new ToggleFeatureFlag.Command
        {
            Id = id,
            IsEnabled = isEnabled!.Value,
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("admin/virtual-accounts")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(
        typeof(BaseResult<CreateVirtualAccountForUserResponseDto>),
        (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> CreateVirtualAccountForUser(
        [FromBody] CreateVirtualAccountForUserCommand.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("banks/refresh")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
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

    [HttpGet("get/users/{userPublicId}/roles")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetUserRolesQuery.GetUserRolesResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetUserRoles(
        [FromRoute] string userPublicId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetUserRolesQuery.Query { UserPublicId = userPublicId },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("add/users/{userPublicId}/roles")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<AddUserToRoleCommand.AssignRoleResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> AddUserToRole(
        [FromRoute] string userPublicId,
        [FromBody] AddUserToRoleCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = userPublicId ?? string.Empty;
        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpDelete("remove/users/{userPublicId}/roles/{role}")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<RemoveUserFromRoleCommand.AssignRoleResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> RemoveUserFromRole(
        [FromRoute] string userPublicId,
        [FromRoute] string role,
        CancellationToken cancellationToken)
    {
        var command = new RemoveUserFromRoleCommand.Command
        {
            UserPublicId = userPublicId,
            Role = role,
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        return Ok(new
        {
            authenticated = User.Identity?.IsAuthenticated,
            roles = User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToArray(),
        });

    }
}