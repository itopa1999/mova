

using System.Net;
using System.Text;
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

    [HttpPost("flutterwave")]
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

    

    // Print signature specifically
    var signature = Request.Headers["Verif-Hash"]
        .FirstOrDefault();

    Console.WriteLine("\nFLUTTERWAVE SIGNATURE:");
    Console.WriteLine(signature ?? "NULL");

    // Print complete request body
    Console.WriteLine("\nREQUEST BODY:");
    Console.WriteLine(
        Encoding.UTF8.GetString(rawBody));

    Console.WriteLine("\n========================================");

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