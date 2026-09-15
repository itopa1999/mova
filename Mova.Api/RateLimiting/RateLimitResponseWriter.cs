using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Mova.Api.RateLimiting;

internal static class RateLimitResponseWriter
{
    public static async ValueTask OnRejected(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        httpContext.Response.StatusCode =
            StatusCodes.Status429TooManyRequests;

        httpContext.Response.ContentType = "application/json";

        var retryAfter = TimeSpan.Zero;

        if (context.Lease.TryGetMetadata(
                MetadataName.RetryAfter,
                out var retryAfterValue))
        {
            retryAfter = retryAfterValue;
        }

        var retryAfterSeconds =
            (int)Math.Ceiling(retryAfter.TotalSeconds);

        var retryAfterText =
            FormatRetryAfter(retryAfterSeconds);

        httpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString();

        httpContext.Response.Headers["X-RateLimit-RetryAfter"] =
            retryAfterSeconds.ToString();

        await httpContext.Response.WriteAsJsonAsync(new
        {
            is_success = false,
            status_code = "rateLimitExceeded",
            message =
                $"Too many requests. Try again in {retryAfterText}.",
            retry_after_seconds = retryAfterSeconds,
            timestamp = DateTimeOffset.UtcNow,
        }, cancellationToken);
    }

    private static string FormatRetryAfter(int seconds)
    {
        if (seconds <= 0)
            return "a moment";

        if (seconds < 60)
            return seconds == 1
                ? "1 second"
                : $"{seconds} seconds";

        var minutes = (int)Math.Ceiling(seconds / 60.0);

        return minutes == 1
            ? "1 minute"
            : $"{minutes} minutes";
    }
}