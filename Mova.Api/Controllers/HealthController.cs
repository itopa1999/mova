using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Mova.Api.HealthChecks;

namespace Mova.Api.Controllers;

/// <summary>Provides backend readiness status for deployment probes.</summary>
[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthController(HealthCheckService healthCheckService) : ControllerBase
{
    /// <summary>Checks whether the backend dependencies and required data are ready.</summary>
    [HttpGet("ready")]
    [ProducesResponseType(typeof(BackendReadinessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BackendReadinessResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetReadiness(
        CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(
            cancellationToken: cancellationToken);
        var checks = report.Entries
            .SelectMany(entry => entry.Value.Data)
            .Select(item =>
            {
                var result = (BackendReadinessHealthCheck.ReadinessCheckResult)item.Value;
                return new BackendReadinessCheckResponse(
                    item.Key,
                    result.Status,
                    result.Message);
            })
            .ToArray();

        var response = new BackendReadinessResponse(
            report.Status.ToString(),
            checks);

        return report.Status == HealthStatus.Healthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}

/// <summary>Overall backend readiness and the results of its component checks.</summary>
public sealed record BackendReadinessResponse(
    string Status,
    IReadOnlyList<BackendReadinessCheckResponse> Checks);

/// <summary>The outcome and safe diagnostic message for one readiness check.</summary>
public sealed record BackendReadinessCheckResponse(
    string Name,
    string Status,
    string Message);
