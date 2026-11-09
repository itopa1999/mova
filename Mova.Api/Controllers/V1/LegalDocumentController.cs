using System.ComponentModel.DataAnnotations;
using System.Net;
using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mova.Api.Configurations;
using Mova.Application.BBL.Commands.Legal;
using Mova.Application.BBL.Queries.Legal;
using Mova.Domain.Enums;
using Mova.Shared.Common.Results;
using Mova.Shared.Constants;

namespace Mova.Api.Controllers.V1;

[ApiController]
[Route("api/v{version:apiVersion}/legal")]
[ApiVersion("1.0")]
public class LegalDocumentController(IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    [HttpGet("current")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(BaseResult<CurrentLegalDocumentsResponse>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.InternalServerError)]
    public async Task<IActionResult> GetCurrentDocuments(
        [FromQuery] LegalDocumentType? type,
        CancellationToken cancellationToken)
    {
        var query = new GetCurrentLegalDocumentsQuery.Query { Type = type };
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("accept/agreement")]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(BaseResult<AcceptConsentResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Conflict)]
    public async Task<IActionResult> AcceptAgreement(
        [FromBody] AcceptLegalDocumentBody body,
        CancellationToken cancellationToken)
    {
        var command = new AcceptLegalDocumentCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            Type = LegalDocumentType.Agreement,
            DocumentVersion = body.DocumentVersion,
        };
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("accept/privacy")]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(BaseResult<AcceptConsentResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Conflict)]
    public async Task<IActionResult> AcceptPrivacy(
        [FromBody] AcceptLegalDocumentBody body,
        CancellationToken cancellationToken)
    {
        var command = new AcceptLegalDocumentCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            Type = LegalDocumentType.Privacy,
            DocumentVersion = body.DocumentVersion,
        };
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }

    [HttpPost("accept/terms")]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(BaseResult<AcceptConsentResponseDto>), (int)HttpStatusCode.OK)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.BadRequest)]
    [ProducesResponseType(typeof(BaseResult), (int)HttpStatusCode.Conflict)]
    public async Task<IActionResult> AcceptTerms(
        [FromBody] AcceptLegalDocumentBody body,
        CancellationToken cancellationToken)
    {
        var command = new AcceptLegalDocumentCommand.Command
        {
            UserPublicId = UserPublicId ?? string.Empty,
            Type = LegalDocumentType.Terms,
            DocumentVersion = body.DocumentVersion,
        };
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode((int)result.StatusCode, result);
    }
}

public sealed class AcceptLegalDocumentBody
{
    [Required, MaxLength(20)]
    public string DocumentVersion { get; set; } = string.Empty;
}