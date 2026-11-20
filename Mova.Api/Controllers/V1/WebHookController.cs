using System.Net;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.WebHook;
using Mova.Shared.Common.Results;
using Asp.Versioning;
using Mova.Api.Filters;
using Mova.Shared.Constants;

namespace Mova.Api.Controllers.V1;

/// <summary>
/// Inbound payment-provider webhook endpoints: Paystack and Flutterwave.
/// </summary>
/// <remarks>
/// External-only. Requests are authenticated by provider HMAC signature
/// (verified inside the mediator handler), not by JWT. A coarse
/// <see cref="RateLimitPolicies.Read"/> cap is applied purely as an abuse
/// safety net — it is generous enough to absorb normal provider bursts
/// while still preventing a single misbehaving source from flooding the
/// API. Do not tighten these further: dropping legitimate webhooks breaks
/// payment reconciliation, and payment providers retry on 429.
/// </remarks>
[ApiController]
[Route("api/v{version:apiVersion}/webhook")]
[ApiVersion("1.0")]
[RestrictPlatform(Platforms.External)]
public class WebHookController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    // ----------------------- Commands -----------------------

    /// <summary>
    /// Receives Paystack payment and transfer events. Authenticates the
    /// request via the <c>x-paystack-signature</c> header against the raw
    /// body, then dispatches to the handler for reconciliation.
    /// </summary>
    [HttpPost("paystack")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> PaystackWebHook(
        CancellationToken cancellationToken)
    {
        using var bodyStream = new MemoryStream();
        await Request.Body.CopyToAsync(bodyStream, cancellationToken);

        var signature = Request.Headers["x-paystack-signature"].FirstOrDefault();

        var command = new PaystackWebHookCommand.Command
        {
            RawBody = bodyStream.ToArray(),
            Signature = signature
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    /// <summary>
    /// Receives Flutterwave payment and transfer events. Authenticates the
    /// request via the <c>Verif-Hash</c> header against the raw body, then
    /// dispatches to the handler for reconciliation.
    /// </summary>
    [HttpPost("flutterwave")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> FlutterwaveWebHook(
        CancellationToken cancellationToken)
    {
        using var bodyStream = new MemoryStream();

        await Request.Body.CopyToAsync(
            bodyStream,
            cancellationToken);

        var rawBody = bodyStream.ToArray();

        var signature = Request.Headers["Verif-Hash"]
            .FirstOrDefault();

        var command = new FlutterwaveWebHookCommand.Command
        {
            RawBody = rawBody,
            Signature = signature
        };

        var result = await _mediator.Send(
            command,
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }
}