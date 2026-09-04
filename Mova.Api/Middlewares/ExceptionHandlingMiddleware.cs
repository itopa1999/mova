using System.Net;
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

        // Determine status code and message based on exception type
        var (statusCode, message) = GetExceptionDetails(exception);

        context.Response.StatusCode = (int)statusCode;
        context.Response.Headers["X-Request-Id"] = requestId;

        // Create result using your existing BaseResult
        var result = new BaseResult(
            statusCode: statusCode,
            message: message,
            requestId: requestId
        );

        await context.Response.WriteAsJsonAsync(result);
    }

    private (HttpStatusCode statusCode, string message) GetExceptionDetails(Exception exception)
    {
        // Handle ArgumentException (including PIN validation, etc.)
        if (exception is ArgumentException argEx)
        {
            return (HttpStatusCode.BadRequest, argEx.Message);
        }

        // Handle ArgumentNullException
        if (exception is ArgumentNullException nullEx)
        {
            return (HttpStatusCode.BadRequest, nullEx.Message);
        }

        // Handle ArgumentOutOfRangeException
        if (exception is ArgumentOutOfRangeException outOfRangeEx)
        {
            return (HttpStatusCode.BadRequest, outOfRangeEx.Message);
        }

        // Handle InvalidOperationException
        if (exception is InvalidOperationException invalidOpEx)
        {
            return (HttpStatusCode.BadRequest, invalidOpEx.Message);
        }

        // Handle FluentValidation.ValidationException
        if (exception.GetType().Name == "ValidationException")
        {
            var errors = exception.GetType().GetProperty("Errors")?.GetValue(exception) as IEnumerable<dynamic>;
            var errorMessages = errors != null 
                ? string.Join(" | ", errors.Select(e => e?.ErrorMessage?.ToString() ?? e?.ToString() ?? "Validation error"))
                : exception.Message;
            
            return (HttpStatusCode.BadRequest, errorMessages);
        }

        // Handle UnauthorizedAccessException
        if (exception is UnauthorizedAccessException)
        {
            return (HttpStatusCode.Unauthorized, "You are not authorized to perform this action.");
        }

        // Handle KeyNotFoundException (Not Found)
        if (exception is KeyNotFoundException)
        {
            return (HttpStatusCode.NotFound, "The requested resource was not found.");
        }

        // Handle DbUpdateException (Database errors)
        if (exception.GetType().Name == "DbUpdateException" || 
            exception.GetType().Name == "DbUpdateConcurrencyException")
        {
            return (HttpStatusCode.Conflict, "An error occurred. Please try again.");
        }

        // Handle TimeoutException
        if (exception is TimeoutException)
        {
            return (HttpStatusCode.RequestTimeout, "The request timed out. Please try again.");
        }

        // Handle NotImplementedException
        if (exception is NotImplementedException)
        {
            return (HttpStatusCode.NotImplemented, "This feature is not yet implemented.");
        }

        // Default to Internal Server Error for unhandled exceptions
        return (
            HttpStatusCode.InternalServerError,
            "An error occurred; please try again later"
        );
    }
}