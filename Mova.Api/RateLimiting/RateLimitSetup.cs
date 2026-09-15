using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Mova.Api.RateLimiting;

public static class RateLimitSetup
{
    public static IServiceCollection AddAppRateLimiting(
        this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode =
                StatusCodes.Status429TooManyRequests;

            options.OnRejected = RateLimitResponseWriter.OnRejected;

            // Global fallback — applied when an endpoint has no
            // explicit policy. Partitioned by user (if authenticated)
            // or IP.
            options.GlobalLimiter =
                PartitionedRateLimiter.Create<HttpContext, string>(
                    httpContext => BuildPartition(
                        httpContext,
                        permitLimit: 100,
                        window: TimeSpan.FromMinutes(1)));

            // ------------------- Auth: strict -------------------
            options.AddPolicy(
                RateLimitPolicies.AuthStrict,
                httpContext => BuildPartition(
                    httpContext,
                    permitLimit: 5,
                    window: TimeSpan.FromMinutes(1)));

            // ------------------- Auth: medium -------------------
            options.AddPolicy(
                RateLimitPolicies.AuthMedium,
                httpContext => BuildPartition(
                    httpContext,
                    permitLimit: 20,
                    window: TimeSpan.FromMinutes(1)));

            // ------------------- Read -------------------
            options.AddPolicy(
                RateLimitPolicies.Read,
                httpContext => BuildPartition(
                    httpContext,
                    permitLimit: 120,
                    window: TimeSpan.FromMinutes(1)));

            // ------------------- Write -------------------
            options.AddPolicy(
                RateLimitPolicies.Write,
                httpContext => BuildPartition(
                    httpContext,
                    permitLimit: 30,
                    window: TimeSpan.FromMinutes(1)));

            // ------------------- Sensitive -------------------
            options.AddPolicy(
                RateLimitPolicies.Sensitive,
                httpContext => BuildPartition(
                    httpContext,
                    permitLimit: 10,
                    window: TimeSpan.FromMinutes(1)));
        });

        return services;
    }

    private static RateLimitPartition<string> BuildPartition(
        HttpContext httpContext,
        int permitLimit,
        TimeSpan window)
    {
        var userPublicId = httpContext.User?.FindFirst("sub")?.Value;

        var partitionKey = !string.IsNullOrEmpty(userPublicId)
            ? $"user:{userPublicId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
    }
}