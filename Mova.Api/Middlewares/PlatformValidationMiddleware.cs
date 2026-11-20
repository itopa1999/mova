using System.Net;
using Mova.Shared.Common.Results;
using Mova.Shared.Constants;

namespace Mova.Api.Middlewares;

public class PlatformValidationMiddleware
{
    private readonly RequestDelegate _next;
    private const string HeaderName = "X-Platform";

    private static readonly HashSet<string> ValidPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        Platforms.Web,
        Platforms.Mobile,
        Platforms.Swagger
    };

    private static readonly string[] ExternalPaths = 
    { 
        "/api/v1/webhooks",
        "/api/v1/bank-account/payment/callback"
    };

    private static readonly string[] InternalPaths = 
    { 
        "/health", 
        "/swagger", 
        "/hangfire",
        "api/v1/auth/verify-account"
    };

    public PlatformValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (MatchesPath(path, ExternalPaths))
        {
            context.Items["Platform"] = Platforms.External; 
            await _next(context);
            return;
        }

        if (MatchesPath(path, InternalPaths))
        {
    
            context.Items["Platform"] = Platforms.Internal; 
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var platformHeader))
        {
            await WriteErrorResponse(context, "Missing X-Platform header.");
            return;
        }

        var platform = platformHeader.ToString();

        if (!ValidPlatforms.Contains(platform))
        {
            await WriteErrorResponse(context, "Invalid X-Platform header value.");
            return;
        }

        context.Items["Platform"] = platform;

        await _next(context);
    }

    private static bool MatchesPath(string path, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task WriteErrorResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        context.Response.ContentType = "application/json";

        var result = new BaseResult(
            statusCode: HttpStatusCode.BadRequest,
            message: message
        ) 
        { 
            Platform = "Unknown" 
        };

        await context.Response.WriteAsJsonAsync(result);
    }
}