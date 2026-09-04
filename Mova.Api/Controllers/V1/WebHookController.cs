

using System.Net;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Mova.Api.Configurations;
using Mova.Application.BBL.Commands.WebHook;
using Mova.Shared.Common;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Route("api/v1/webhook")]
[ApiExplorerSettings(GroupName = "v1")]
public class WebHookController(
    IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;


    [HttpPost("paystack")]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> PaystackWebHook(
        CancellationToken cancellationToken)
    {
        Request.EnableBuffering();

        using var reader = new StreamReader(Request.Body);

        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        Request.Body.Position = 0;

        var signature = Request.Headers["x-paystack-signature"].FirstOrDefault();

        var command = new PaystackWebHookCommand.Command
        {
            RawBody = rawBody,
            Signature = signature
        };

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }
}