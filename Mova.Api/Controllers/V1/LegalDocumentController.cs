using System.ComponentModel.DataAnnotations;
using System.Net;
using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Api.Configurations;
using Mova.Api.Filters;
using Mova.Api.RateLimiting;
using Mova.Application.BBL.Commands.Legal;
using Mova.Application.BBL.Queries.Legal;
using Mova.Domain.Enums;
using Mova.Shared.Common.Results;
using Mova.Shared.Constants;

namespace Mova.Api.Controllers.V1;

/// <summary>
/// Legal-document endpoints: reading the current agreement, privacy policy,
/// and terms, and recording a customer's acceptance of each.
/// </summary>
/// <remarks>
/// Reads use <see cref="RateLimitPolicies.Read"/>; acceptance mutations use
/// <see cref="RateLimitPolicies.Write"/>.
/// </remarks>
[ApiController]
[Route("api/v{version:apiVersion}/legal")]
[ApiVersion("1.0")]
[RestrictPlatform(Platforms.Web, Platforms.Swagger)]
public class LegalDocumentController(IMediator mediator) : BaseController
{
    private readonly IMediator _mediator = mediator;

    // ----------------------- Commands -----------------------

    /// <summary>
    /// Records the authenticated customer's acceptance of the current
    /// agreement document version.
    /// </summary>
    [HttpPost("accept/agreement")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Write)]
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

    /// <summary>
    /// Records the authenticated customer's acceptance of the current
    /// privacy-policy document version.
    /// </summary>
    [HttpPost("accept/privacy")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Write)]
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

    /// <summary>
    /// Records the authenticated customer's acceptance of the current
    /// terms-of-service document version.
    /// </summary>
    [HttpPost("accept/terms")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting(RateLimitPolicies.Write)]
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

    // ----------------------- Queries -----------------------

    /// <summary>
    /// Returns the current versions of the legal documents, optionally
    /// filtered by document type.
    /// </summary>
    [HttpGet("current")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Read)]
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
}

public sealed class AcceptLegalDocumentBody
{
    [Required, MaxLength(20)]
    public string DocumentVersion { get; set; } = string.Empty;
}