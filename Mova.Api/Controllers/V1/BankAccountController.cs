using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.BBL.Commands.BanksAccount;
using Mova.Application.BBL.Queries.BanksAccount;
using Mova.Infrastructure.ExternalAPI;
using Mova.Shared.Common;
using static Mova.Application.BBL.Commands.AccountWallet.LinkAccountToBank;
using static Mova.Application.BBL.Commands.BanksAccount.AddBankAccount;
using static Mova.Application.BBL.Commands.BanksAccount.FundAccount;
using static Mova.Application.BBL.Commands.BanksAccount.VerifyBankAccount;
using static Mova.Application.BBL.Queries.BanksAccount.DepositTransaction;
using static Mova.Application.BBL.Queries.BanksAccount.GetAllBankAccount;
using static Mova.Application.BBL.Queries.BanksAccount.GetBanks;
using static Mova.Application.BBL.Queries.BanksAccount.GetTransactions;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize]
[Route("api/v1/bank-account")]
[ApiExplorerSettings(GroupName = "v1")]
public class BankAccountController(
    IMediator mediator,
    IOptions<ExternalApiSettings> externalApiSettings) : BaseController
{
    private readonly IMediator _mediator = mediator;
    private readonly ExternalApiSettings _externalApiSettings = externalApiSettings.Value;

    [HttpGet("banks")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<List<GetBanksDto>>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetBanksDetailsData([FromQuery] string? name, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetBanks.Query
            {
                Name = name
            },
            cancellationToken);

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

    [HttpPost("banks/verify")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<VerifyBankAccountDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyBankAccount(
        [FromBody] VerifyBankAccount.Command command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            command,
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<AddBankAccountDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> AddBankAccount(
        [FromBody] AddBankAccount.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;
        command.FirstName  = UserFirstName ?? string.Empty;

        var result = await _mediator.Send(
            command,
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("{walletId:long}/bank-account")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(
        typeof(BaseResult<LinkAccountToBankDto>),
        (int)HttpStatusCode.OK)]
    [ProducesResponseType(
        typeof(BaseResult),
        (int)HttpStatusCode.NotFound)]
    public async Task<IActionResult> LinkAccountToBank(
        long walletId,
        [FromBody] LinkAccountToBank.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.WalletId = walletId;
        command.FirstName = UserFirstName ?? string.Empty;
        command.Email = UserEmail ?? string.Empty;

        var result = await _mediator.Send(
            command,
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult<List<GetAllBankAccountDto>>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> GetBankAccounts(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetAllBankAccount.Query
            {
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }


    [HttpDelete("{bankAccountId:long}/remove")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> RemoveBankAccounts(
        long bankAccountId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DeleteBankAccount.Command
            {
                UserPublicId = UserPublicId ?? string.Empty,
                BankAccountId = bankAccountId
            },
            cancellationToken);

        return StatusCode((int)result.StatusCode, result);
    }



    [HttpGet("deposits")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(
        typeof(BaseResult<List<DepositTransaction.TransactionDto>>),
        (int)HttpStatusCode.OK)]
    public async Task<IActionResult> GetUnassignedDepositTransactions(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new DepositTransaction.Query
            {
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpGet("transactions")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(
        typeof(BaseResult<PaginatedTransactionsDto>),
        (int)HttpStatusCode.OK)]
    public async Task<IActionResult> GetTransactions([FromQuery] GetTransactions.Query query)
    {
        query.UserPublicId = UserPublicId ?? string.Empty;
        var result = await _mediator.Send(query);
        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("fund-account")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult<FundAccountDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> FundAccount(
        [FromBody] FundAccount.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    [HttpGet("payment/callback")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType((int)HttpStatusCode.Redirect)]
    public async Task<IActionResult> PaymentCallback(
        [FromQuery] string? reference,
        [FromQuery(Name = "tx_ref")] string? txRef,
        [FromQuery(Name = "paymentReference")] string? monnifyReference,
        CancellationToken cancellationToken)
    {
        var paymentReference =
        !string.IsNullOrWhiteSpace(reference)
            ? reference
            : !string.IsNullOrWhiteSpace(txRef)
                ? txRef
                : monnifyReference;

        var result = await _mediator.Send(
            new PaymentCallback.Query
            {
                Reference = paymentReference
            },
            cancellationToken);

        var frontendUrl = _externalApiSettings.FrontendBaseUrl;

        if (result.Data is null)
        {
            var failedRedirectUrl = QueryHelpers.AddQueryString(
                $"{frontendUrl}/payment/confirmation",
                new Dictionary<string, string?>
                {
                    ["reference"] = paymentReference,
                    ["status"] = "NotFound"
                });

            return Redirect(failedRedirectUrl);
        }

        var redirectUrl = QueryHelpers.AddQueryString(
            $"{frontendUrl}/payment/confirmation",
            new Dictionary<string, string?>
            {
                ["reference"] = result.Data.Reference,
                ["amount"] = result.Data.Amount.ToString(),
                ["createdAt"] = result.Data.CreatedAt?.ToString("O"),
                ["status"] = result.Data.Status
            });

        return Redirect(redirectUrl);
    }
}