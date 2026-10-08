using System.Net;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.RateLimiting;
using Mova.Api.Security;
using Mova.Application.BBL.Commands.TransactionPin;
using Mova.Application.BBL.Queries.TransactionPin;
using Mova.Shared.Common;
using Mova.Shared.Constants;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Authorize(Roles = Roles.Customer)]
[Route("api/v1/security/pin")]
[ApiExplorerSettings(GroupName = "v1")]
public class TransactionPinController(
    IMediator mediator,
    IPinDecryptionService pinDecryptionService,
    ILogger<TransactionPinController> logger) : BaseController
{
    private readonly IMediator _mediator = mediator;
    private readonly IPinDecryptionService _pinDecryptionService = pinDecryptionService;
    private readonly ILogger<TransactionPinController> _logger = logger;

    [HttpPost("set")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> SetPin(
        [FromBody] EncryptedPinRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryDecryptPin(request.Pin, out var pin))
        {
            return InvalidEncryptedPin();
        }

        var command = new SetPinCommand.Command
        {
            Pin = pin
        };
        command.UserPublicId = UserPublicId ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("verify")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Locked)]
    public async Task<IActionResult> VerifyPin(
        [FromBody] EncryptedPinRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryDecryptPin(request.Pin, out var pin))
        {
            return InvalidEncryptedPin();
        }

        var command = new VerifyPinCommand.Command
        {
            Pin = pin
        };
        command.UserPublicId = UserPublicId ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPut("change")]
    [EnableRateLimiting(RateLimitPolicies.Sensitive)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> ChangePin(
        [FromBody] ChangeEncryptedPinRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryDecryptPin(request.CurrentPin, out var currentPin)
            || !TryDecryptPin(request.NewPin, out var newPin))
        {
            return InvalidEncryptedPin();
        }

        var command = new ChangePinCommand.Command
        {
            CurrentPin = currentPin,
            NewPin = newPin
        };
        command.UserPublicId = UserPublicId ?? string.Empty;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);

    }

    [HttpGet("has-pin-setup")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    public async Task<IActionResult> CheckIfPinSet(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetIfPinIsSetQuery.Query
            {
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);

    }


    [HttpPost("forgot-pin-send")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> SendOtpForPinForgot(
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SendForgotPinOtpCommand.Command
            {
                UserPublicId = UserPublicId ?? string.Empty
            },
            cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }

    [HttpPost("forgot-pin-verify")]
    [EnableRateLimiting(RateLimitPolicies.AuthStrict)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    public async Task<IActionResult> VerifyOtpForPinForgot(
        [FromBody] VerifyForgotPinOtpCommand.Command command,
        CancellationToken cancellationToken)
    {
        command.UserPublicId = UserPublicId ?? string.Empty;
        command.UserId = CurrentUserId ?? 0;

        var result = await _mediator.Send(command, cancellationToken);

        return StatusCode(
            (int)result.StatusCode,
            result);
    }


    private bool TryDecryptPin(string encryptedPin, out string pin)
    {
        try
        {
            pin = _pinDecryptionService.Decrypt(encryptedPin);
            return true;
        }
        catch (PinDecryptionException exception)
        {
            _logger.LogWarning(exception, "An invalid encrypted transaction PIN was received.");
            pin = string.Empty;
            return false;
        }
    }

    private static IActionResult InvalidEncryptedPin() =>
        new BadRequestObjectResult(new BaseResult(
            HttpStatusCode.BadRequest,
            "Encrypted transaction PIN is invalid."));
}