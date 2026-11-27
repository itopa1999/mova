using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Mova.Shared.Common.Results;
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

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue))
        {
            retryAfter = retryAfterValue;
        }

        if (retryAfter <= TimeSpan.Zero)
        {
            retryAfter = TimeSpan.FromMinutes(1);
        }

        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        var retryAfterText =
            FormatRetryAfter(retryAfterSeconds);

        httpContext.Response.Headers.RetryAfter =
            retryAfterSeconds.ToString();

        httpContext.Response.Headers["X-RateLimit-RetryAfter"] =
            retryAfterSeconds.ToString();

        await httpContext.Response.WriteAsJsonAsync(
            new BaseResult<object>(HttpStatusCode.TooManyRequests,
        $"Too many requests. Try again in {retryAfterText}.",
        new 
        { 
            RetryAfterSeconds = retryAfterSeconds 
        }),
        cancellationToken);
    }

    private static string FormatRetryAfter(int seconds)
    {
        if (seconds <= 0) return "a moment";
        if (seconds == 1) return "1 second";
        if (seconds < 60) return $"{seconds} seconds";

        var span = TimeSpan.FromSeconds(seconds);

        if (span.TotalDays >= 1 && span.Hours == 0 && span.Minutes == 0)
            return span.Days == 1 ? "1 day" : $"{span.Days} days";

        var parts = new List<string>();

        if (span.Days > 0)
            parts.Add(span.Days == 1 ? "1 day" : $"{span.Days} days");

        if (span.Hours > 0)
            parts.Add(span.Hours == 1 ? "1 hour" : $"{span.Hours} hours");

        if (span.Minutes > 0)
            parts.Add(span.Minutes == 1 ? "1 minute" : $"{span.Minutes} minutes");

        if (span.TotalHours < 1 && span.Seconds > 0)
            parts.Add(span.Seconds == 1 ? "1 second" : $"{span.Seconds} seconds");

        return string.Join(" ", parts);
    }
}