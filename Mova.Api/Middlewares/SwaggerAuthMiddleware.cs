using System.Security.Cryptography;
using System.Text;
using Mova.Api.Configurations;
using Mova.Shared.Logging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Mova.Api.Middlewares;

public class SwaggerAuthMiddleware
{
    private const string CookieName = "SwaggerAuth";

    private readonly RequestDelegate _next;
    private readonly SwaggerSettings _settings;
    private readonly ILogger<SwaggerAuthMiddleware> _logger;
    private readonly IDataProtector _protector;

    public SwaggerAuthMiddleware(
        RequestDelegate next,
        IOptions<SwaggerSettings> options,
        IDataProtectionProvider protectionProvider,
        ILogger<SwaggerAuthMiddleware> logger)
    {
        _next = next;
        _settings = options.Value;
        _logger = logger;

        _protector = protectionProvider.CreateProtector("SwaggerAuthenticationCookie");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        using var op = OperationLogger.Start(
            _logger,
            "SwaggerAuth",
            ("Path", context.Request.Path.ToString()),
            ("Method", context.Request.Method),
            ("IP", context.Connection.RemoteIpAddress?.ToString() ?? "unknown"));

        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (IsAuthenticated(context))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            await Challenge(context);
            return;
        }

        var header = authHeader.ToString();

        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            await Challenge(context);
            return;
        }

        try
        {
            var encoded = header["Basic ".Length..].Trim();

            var decoded = Encoding.UTF8.GetString(
                Convert.FromBase64String(encoded));

            var credentials = decoded.Split(':', 2);

            if (credentials.Length != 2)
            {
                await Challenge(context);
                return;
            }

            var username = credentials[0];
            var password = credentials[1];

            if (!SecureEquals(username, _settings.Username) ||
                !SecureEquals(password, _settings.Password))
            {
                op.Fail("Failed Swagger login attempt.");

                await Challenge(context);
                return;
            }

            SetAuthenticationCookie(context);

            op.Success("Swagger login successful.");

            await _next(context);
        }
        catch (Exception ex)
        {
            op.Fail("Invalid Swagger Authorization header.", ex);

            await Challenge(context);
        }
    }

    private bool IsAuthenticated(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var cookie))
            return false;

        try
        {
            var value = _protector.Unprotect(cookie);

            return value == "Authenticated";
        }
        catch
        {
            return false;
        }
    }

    private void SetAuthenticationCookie(HttpContext context)
    {
        var protectedValue = _protector.Protect("Authenticated");

        context.Response.Cookies.Append(
            CookieName,
            protectedValue,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddHours(1)
            });
    }

    private static async Task Challenge(HttpContext context)
    {
        context.Response.Headers["WWW-Authenticate"] =
            "Basic realm=\"Mova Swagger\"";

        context.Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        await context.Response.WriteAsync(
            "Authentication required.");
    }

    private static bool SecureEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        if (leftBytes.Length != rightBytes.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            leftBytes,
            rightBytes);
    }
}