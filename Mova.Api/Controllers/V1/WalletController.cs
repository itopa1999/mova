using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.BBL.Queries.SchedulePreview;
using Mova.Shared.Common;
using static Mova.Application.BBL.Commands.AccountWallet.CreateWalletCommand;
using static Mova.Application.BBL.MovaAPIs.GetReleasesQuery;
using static Mova.Application.BBL.Queries.AccountWallet.GetAllWallets;
using static Mova.Application.BBL.Queries.AccountWallet.GetWalletActivities;
using static Mova.Application.BBL.Queries.AccountWallet.GetWalletAnalytics;
using static Mova.Application.BBL.Queries.AccountWallet.GetWalletPayouts;
using static Mova.Application.BBL.Queries.AccountWallet.GetWalletSchedulePreviewQuery;
using static Mova.Application.BBL.Queries.AccountWallet.WalletDetails;
using static Mova.Application.BBL.Queries.SchedulePreview.SchedulePreviewQuery;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/wallets")]
[ApiExplorerSettings(GroupName = "v1")]
public class WalletController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    [HttpPost("create")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<CreateWalletResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> CreateWallet([FromBody] CreateWalletCommand.Command command, CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;
        command.FirstName = UserFirstName ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("{walletId:long}/relock-unused")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
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
    [EnableRateLimiting(RateLimitPolicies.Read)]
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

    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetAllWalletsResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetAllWallets([FromQuery] int page = 1, [FromQuery]int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetAllWallets.Query
            {
                Page = page,
                PageSize = pageSize,
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("{walletId:long}/details")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<WalletDetailsResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetWalletDetails(long walletId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new WalletDetails.Query
            {
                WalletId = walletId,
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("{walletId:long}/activities")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<List<WalletActivityGroupDto>>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetWalletActivities(
        long walletId,
        CancellationToken cancellationToken)
    {
        var query = new GetWalletActivities.Query
        {
            WalletId = walletId,
            UserPublicId = UserPublicId ?? string.Empty
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("{walletId:long}/payouts")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<List<WalletPayoutGroupDto>>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetWalletPayouts(
        long walletId,
        CancellationToken cancellationToken)
    {
        var query = new GetWalletPayouts.Query
        {
            WalletId = walletId,
            UserPublicId = UserPublicId ?? string.Empty
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("analytics")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<WalletAnalyticsDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetWalletAnalytics(
        [FromQuery] DateTime? date,
        CancellationToken cancellationToken)
    {
        var query = new GetWalletAnalytics.Query
        {
            UserPublicId = UserPublicId ?? string.Empty,
            Date = date
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("categories")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<List<GetWalletCategories.WalletCategoryDto>>),(int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetWalletCategories(
        CancellationToken cancellationToken)
    {
        var query = new GetWalletCategories.Query();

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("{walletId:long}/bank-account")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(
        typeof(BaseResult<GetWalletBankAccount.BankAccountDto>),
        (int)HttpStatusCode.OK)]
    [ProducesResponseType(
        typeof(BaseResult),
        (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetWalletBankAccount(
        long walletId,
        CancellationToken cancellationToken)
    {
        var query = new GetWalletBankAccount.Query
        {
            UserPublicId = UserPublicId ?? string.Empty,
            WalletId = walletId
        };

        var result = await _mediator.Send(
            query,
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpGet("releases")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetReleasesQueryDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult<GetReleasesQueryDto>), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetReleases(
        [FromQuery] int upcomingLimit = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new GetReleasesQuery.Query
        {
            UserPublicId = UserPublicId ?? string.Empty,
            UpcomingLimit = upcomingLimit
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("preview")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<SchedulePreviewResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> PreviewSchedule(
        [FromBody] SchedulePreviewQuery.Query query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPut("{walletId:long}/break")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> BreakWallet(
        long walletId,
        CancellationToken cancellationToken)
    {
        var command = new BreakWalletCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            Email = UserEmail ?? string.Empty,
            FirstName = UserFirstName ?? string.Empty,
            WalletId = walletId,
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPut("{walletId:long}/toggle-status")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> ToggleWalletStatus(
        [FromRoute] long walletId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ToggleWalletStatusCommand.Command
            {
                UserPublicId = UserPublicId ?? string.Empty,
                WalletId = walletId,
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }
}