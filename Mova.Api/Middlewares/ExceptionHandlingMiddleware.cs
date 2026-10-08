using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Mova.Shared.Logging;
using Mova.Shared.Common;

namespace Mova.Api.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.TraceIdentifier;
        context.Items["RequestId"] = requestId;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex, requestId);
        }
    }

    private async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        string requestId)
    {
        using var op = OperationLogger.Start(
            _logger,
            "ExceptionHandling",
            ("RequestId", requestId),
            ("Path", context.Request.Path.ToString()),
            ("Method", context.Request.Method));

        if (context.Response.HasStarted)
        {
            op.Fail("Response already started", exception);
            return;
        }

        op.Fail("Unhandled exception", exception);

        context.Response.Clear();
        context.Response.ContentType = "application/json";

        var (statusCode, message) = GetExceptionDetails(exception);

        context.Response.StatusCode = (int)statusCode;
        context.Response.Headers["X-Request-Id"] = requestId;

        var result = new BaseResult(
            statusCode: statusCode,
            message: message,
            requestId: requestId
        );

        await context.Response.WriteAsJsonAsync(result);
    }

    private (HttpStatusCode statusCode, string message) GetExceptionDetails(Exception exception)
    {
        if (ContainsException<DbUpdateException>(exception))
        {
            return (
                HttpStatusCode.Conflict,
                "We couldn't save your changes right now. Please try again.");
        }

        if (ContainsException<DbException>(exception))
        {
            return (
                HttpStatusCode.ServiceUnavailable,
                "The service is temporarily unavailable. Please try again shortly.");
        }

        if (exception is ArgumentException)
        {
            return (
                HttpStatusCode.BadRequest,
                "Please check the submitted information and try again.");
        }

        if (exception is InvalidOperationException)
        {
            return (
                HttpStatusCode.BadRequest,
                "This request could not be completed. Please review your information and try again.");
        }

        if (exception.GetType().Name == "ValidationException")
        {
            return (
                HttpStatusCode.BadRequest,
                "Please check the submitted information and try again.");
        }

        if (exception is UnauthorizedAccessException)
        {
            return (HttpStatusCode.Unauthorized, "You are not authorized to perform this action.");
        }

        if (exception is KeyNotFoundException)
        {
            return (HttpStatusCode.NotFound, "The requested resource was not found.");
        }

        if (exception is TimeoutException)
        {
            return (HttpStatusCode.RequestTimeout, "The request timed out. Please try again.");
        }

        if (exception is NotImplementedException)
        {
            return (
                HttpStatusCode.NotImplemented,
                "This feature is not available right now.");
        }

        return (
            HttpStatusCode.InternalServerError,
            "An error occurred. Please try again later."
        );
    }

    private static bool ContainsException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException)
            {
                return true;
            }
        }

        return false;
    }
}