using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Constants;
using static Mova.Application.BBL.Commands.AccountWallet.CreateRenewalPolicyCommand;
using static Mova.Application.BBL.Commands.AccountWallet.CreateWalletCommand;
using static Mova.Application.BBL.Commands.AccountWallet.RestartWalletCommand;
using static Mova.Application.BBL.Commands.AccountWallet.ToggleRenewalCommand;
using static Mova.Application.BBL.Commands.AccountWallet.UpdateRenewalPolicyCommand;
using static Mova.Application.BBL.Queries.AccountWallet.GetAllWallets;
using static Mova.Application.BBL.Queries.AccountWallet.GetReleasesQuery;
using static Mova.Application.BBL.Queries.AccountWallet.GetRenewalEventsQuery;
using static Mova.Application.BBL.Queries.AccountWallet.GetRenewalPolicyQuery;
using static Mova.Application.BBL.Queries.AccountWallet.GetWalletActivities;
using static Mova.Application.BBL.Queries.AccountWallet.GetWalletAnalytics;
using static Mova.Application.BBL.Queries.AccountWallet.GetWalletPayouts;
using static Mova.Application.BBL.Queries.AccountWallet.GetWalletSchedulePreviewQuery;
using static Mova.Application.BBL.Queries.AccountWallet.ListWalletTemplatesQuery;
using static Mova.Application.BBL.Queries.AccountWallet.SchedulePreviewQuery;
using static Mova.Application.BBL.Queries.AccountWallet.WalletDetails;
using Mova.Shared.Common.Results;
using Asp.Versioning;
using Mova.Api.Filters;

namespace Mova.Api.Controllers.V1;

/// <summary>
/// Wallet endpoints: creation, lifecycle (break/restart/toggle), renewal
/// automation, schedule preview, and read-only views (details, activities,
/// payouts, analytics, templates, releases, categories, bank account).
/// </summary>
/// <remarks>
/// Money-moving or financial-state-changing endpoints (break, toggle-status,
/// restart, toggle-renewal) use <see cref="RateLimitPolicies.Sensitive"/>;
/// other mutations use <see cref="RateLimitPolicies.Write"/>; all reads use
/// <see cref="RateLimitPolicies.Read"/>.
/// </remarks>
[ApiController]
[Authorize(Roles = Roles.Customer)]
[Route("api/v{version:apiVersion}/wallets")]
[ApiVersion("1.0")]
[RestrictPlatform(Platforms.Web, Platforms.Swagger)]
public class WalletController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    // ----------------------- Commands -----------------------

    /// <summary>
    /// Creates a new wallet for the authenticated customer.
    /// </summary>
    [HttpPost("create")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<CreateWalletResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> CreateWallet(
        [FromBody] CreateWalletCommand.Command command,
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

    // [HttpPost("{walletId:long}/relock-unused")]
    // [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    // [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    // [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    // public async Task<IActionResult> RelockUnusedFunds(
    //     long walletId,
    //     CancellationToken cancellationToken)
    // {
    //     var command = new RelockUnusedFundsCommand.Command
    //     {
    //         WalletId = walletId,
    //         UserPublicId = UserPublicId ?? string.Empty
    //     };
    //
    //     var result = await _mediator.Send(command, cancellationToken);
    //
    //     return StatusCode(
    //         (int)result.StatusCode,
    //         result);
    // }

    /// <summary>
    /// Breaks (closes out) a wallet and releases its funds.
    /// </summary>
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

    /// <summary>
    /// Toggles the active/paused status of a wallet.
    /// </summary>
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

    /// <summary>
    /// Creates a renewal-automation policy for a wallet.
    /// </summary>
    [HttpPost("{walletId:long}/automation")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<CreateRenewalPolicyResponseDto>), (int)HttpStatusCode.Created)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> CreateRenewalPolicy(
        long walletId,
        [FromBody] CreateRenewalPolicyCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.WalletId = walletId;
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;
        command.FirstName = UserFirstName ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Updates an existing renewal-automation policy for a wallet.
    /// </summary>
    [HttpPut("{walletId:long}/automation")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType(typeof(BaseResult<UpdateRenewalPolicyResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> UpdateRenewalPolicy(
        long walletId,
        [FromBody] UpdateRenewalPolicyCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.WalletId = walletId;
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;
        command.FirstName = UserFirstName ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Toggles the enabled/disabled state of a wallet's renewal automation.
    /// </summary>
    [HttpPut("{walletId:long}/automation/toggle")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<ToggleRenewalResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> ToggleRenewal(
        long walletId,
        CancellationToken cancellationToken)
    {
        var command = new ToggleRenewalCommand.Command
        {
            WalletId = walletId,
            UserPublicId = UserPublicId ?? string.Empty,
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Restarts a previously broken wallet, resuming its financial cycle.
    /// </summary>
    [HttpPut("{walletId:long}/restart")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<RestartWalletResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> RestartWallet(
        long walletId,
        CancellationToken cancellationToken)
    {
        var command = new RestartWalletCommand.Command
        {
            WalletId = walletId,
            UserPublicId = UserPublicId ?? string.Empty,
            Email = UserEmail ?? string.Empty,
            FirstName = UserFirstName ?? string.Empty
        };

        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Computes a schedule preview for the supplied wallet parameters
    /// without persisting anything.
    /// </summary>
    [HttpPost("preview")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<SchedulePreviewResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> PreviewSchedule(
        [FromBody] SchedulePreviewQuery.Query query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    // ----------------------- Queries -----------------------

    /// <summary>
    /// Returns a paginated list of the authenticated customer's wallets.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetAllWalletsResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetAllWallets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string search = "",
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetAllWallets.Query
            {
                Page = page,
                PageSize = pageSize,
                UserPublicId = UserPublicId ?? string.Empty,
                Search = search
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Returns the details of a specific wallet owned by the customer.
    /// </summary>
    [HttpGet("{walletId:long}/details")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<WalletDetailsResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetWalletDetails(
        long walletId,
        CancellationToken cancellationToken)
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

    /// <summary>
    /// Returns the activity timeline for a wallet.
    /// </summary>
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

    /// <summary>
    /// Returns the payout history for a wallet.
    /// </summary>
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

    /// <summary>
    /// Returns the schedule preview for an existing wallet.
    /// </summary>
    [HttpGet("{walletId:long}/schedule-preview")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetWalletSchedulePreviewResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetSchedulePreview(
        long walletId,
        CancellationToken cancellationToken)
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

    /// <summary>
    /// Returns wallet analytics for the authenticated customer, optionally
    /// for a specific date.
    /// </summary>
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

    /// <summary>
    /// Returns the list of wallet categories, optionally filtered by wallet
    /// type.
    /// </summary>
    [HttpGet("categories")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<List<GetWalletCategories.WalletCategoryDto>>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetWalletCategories(
        [FromQuery] WalletType? walletType,
        CancellationToken cancellationToken)
    {
        var query = new GetWalletCategories.Query
        {
            WalletType = walletType
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Returns the bank account linked to a wallet, if any.
    /// </summary>
    [HttpGet("{walletId:long}/bank-account")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetWalletBankAccount.BankAccountDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
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

    /// <summary>
    /// Returns upcoming releases across the customer's wallets.
    /// </summary>
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

    /// <summary>
    /// Returns a paginated list of available wallet templates.
    /// </summary>
    [HttpGet("templates")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<ListWalletTemplatesResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ListWalletTemplates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] long? categoryId = null,
        [FromQuery] WalletType? walletType = null,
        CancellationToken cancellationToken = default)
    {
        var query = new ListWalletTemplatesQuery.Query
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            CategoryId = categoryId,
            WalletType = walletType
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Returns the renewal-automation policy configured for a wallet.
    /// </summary>
    [HttpGet("{walletId:long}/automation")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetRenewalPolicyResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetRenewalPolicy(
        long walletId,
        CancellationToken cancellationToken)
    {
        var query = new GetRenewalPolicyQuery.Query
        {
            WalletId = walletId,
            UserPublicId = UserPublicId ?? string.Empty
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    /// <summary>
    /// Returns the renewal events for a wallet, paginated.
    /// </summary>
    [HttpGet("{walletId:long}/automation/events")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<GetRenewalEventsResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> GetRenewalEvents(
        long walletId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = new GetRenewalEventsQuery.Query
        {
            WalletId = walletId,
            Page = page,
            PageSize = pageSize,
            UserPublicId = UserPublicId ?? string.Empty
        };

        var result = await _mediator.Send(query, cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }
}