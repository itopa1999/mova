
using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.AccountWalletTrustee;
using Mova.Application.BBL.Queries.AccountWalletTrustee;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using static Mova.Application.BBL.Commands.AccountWalletTrustee.CreateTrusteeWalletCommand;
using static Mova.Application.BBL.Commands.AccountWalletTrustee.DeleteTrusteeCommand;
using static Mova.Application.BBL.Commands.AccountWalletTrustee.InviteTrusteeCommand;
using static Mova.Application.BBL.Commands.AccountWalletTrustee.ResendTrusteeInvitationCommand;
using static Mova.Application.BBL.Queries.AccountWalletTrustee.GetReleaseRequestsQuery;
using static Mova.Application.BBL.Queries.AccountWalletTrustee.GetTrusteeConsentQuery;
using static Mova.Application.BBL.Queries.AccountWalletTrustee.GetTrusteeWalletDetailQuery;
using static Mova.Application.BBL.Queries.AccountWalletTrustee.ListOwnerTrusteesQuery;
using static Mova.Application.BBL.Queries.AccountWalletTrustee.ListVerifiedTrusteesQuery;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize(Roles = Roles.Customer)]
[Route("api/v1/trustee/wallets")]
[ApiExplorerSettings(GroupName = "v1")]
public class WalletTrusteeController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    [HttpPost("create")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<CreateTrusteeWalletResponseDto>), (int)HttpStatusCode.Created)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> CreateWallet([FromBody] CreateTrusteeWalletCommand.Command command, CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;
        command.FirstName = UserFirstName ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("invite")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<InviteTrusteeResponseDto>), (int)HttpStatusCode.Created)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Conflict)]
    public async Task<IActionResult> Invite(
        [FromBody] InviteTrusteeCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;
        command.FirstName = UserFirstName ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpGet("trustee")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<ListOwnerTrusteesResponseDto>), (int)HttpStatusCode.OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ListOwnerTrusteesQuery.Query { UserPublicId = UserPublicId ?? string.Empty },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("resend-invitation/{id:long}")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<ResendTrusteeInvitationResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Conflict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Gone)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> Resend(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        var command = new ResendTrusteeInvitationCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            Email = UserEmail ?? string.Empty,
            FirstName = UserFirstName ?? string.Empty,
            TrusteeId = id,
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpDelete("trustee/{id:long}")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<DeleteTrusteeResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult<DeleteTrusteeResponseDto>), (int)HttpStatusCode.Conflict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        var command = new DeleteTrusteeCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            FirstName = UserFirstName ?? string.Empty,
            TrusteeId = id,
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("validate/invitation/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult<TrusteeConsentInfoDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Gone)]
    public async Task<IActionResult> GetConsent(
        [FromRoute] string token,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetTrusteeConsentQuery.Query { Token = token },
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("accept/invitation/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Gone)]
    public async Task<IActionResult> Accept(
        [FromRoute] string token,
        CancellationToken cancellationToken)
    {
        var command = new AcceptTrusteeConsentCommand.Command
        {
            Token = token,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("decline/invitation/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Gone)]
    public async Task<IActionResult> Decline(
        [FromRoute] string token,
        [FromBody] DeclineRequest? body,
        CancellationToken cancellationToken)
    {
        var command = new DeclineTrusteeConsentCommand.Command
        {
            Token = token,
            Reason = body?.Reason,
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    public sealed class DeclineRequest
    {
        [System.ComponentModel.DataAnnotations.MaxLength(500)]
        public string? Reason { get; set; }
    }

    [HttpGet("list/verified/trustees")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<ListVerifiedTrusteesResponseDto>), (int)HttpStatusCode.OK)]
    public async Task<IActionResult> ListVerified(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ListVerifiedTrusteesQuery.Query
            {
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("{id:long}")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<TrusteeWalletDetailDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetDetail(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetTrusteeWalletDetailQuery.Query
            {
                UserPublicId = UserPublicId ?? string.Empty,
                WalletId = id,
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("{id:long}/release-requests")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<ReleaseRequestsPageDto>), (int)HttpStatusCode.OK)]
    public async Task<IActionResult> ListReleaseRequests(
        [FromRoute] long id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetReleaseRequestsQuery.Query
            {
                UserPublicId = UserPublicId ?? string.Empty,
                WalletId = id,
                Page = page,
                PageSize = pageSize,
                Status = status,
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }
}