using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Mova.Api.Middlewares;
using Mova.Shared.Common;

namespace Mova.Tests.MovaAPITest;

public sealed class ExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenDatabaseUpdateFails_ReturnsGenericConflictMessage()
    {
        var response = await InvokeWithExceptionAsync(
            new DbUpdateException("Sensitive database connection details."));

        Assert.Equal((int)HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "We couldn't save your changes right now. Please try again.",
            response.Body.Message);
        Assert.DoesNotContain("Sensitive database connection details.", response.RawBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenDatabaseConnectionFails_ReturnsGenericUnavailableMessage()
    {
        var response = await InvokeWithExceptionAsync(
            new TestDbException("Sensitive data-link provider details."));

        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            "The service is temporarily unavailable. Please try again shortly.",
            response.Body.Message);
        Assert.DoesNotContain("Sensitive data-link provider details.", response.RawBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenDatabaseConnectionFailureIsWrapped_ReturnsGenericUnavailableMessage()
    {
        var response = await InvokeWithExceptionAsync(
            new InvalidOperationException(
                "Query execution failed.",
                new TestDbException("Sensitive data-link provider details.")));

        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            "The service is temporarily unavailable. Please try again shortly.",
            response.Body.Message);
        Assert.DoesNotContain("Sensitive data-link provider details.", response.RawBody);
    }

    [Fact]
    public async Task InvokeAsync_WhenUnexpectedExceptionOccurs_DoesNotReturnExceptionDetails()
    {
        var response = await InvokeWithExceptionAsync(
            new InvalidOperationException("Sensitive backend implementation details."));

        Assert.Equal((int)HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Sensitive backend implementation details.", response.RawBody);
    }

    private static async Task<(int StatusCode, BaseResult Body, string RawBody)> InvokeWithExceptionAsync(
        Exception exception)
    {
        var context = new DefaultHttpContext();
        await using var responseStream = new MemoryStream();
        context.Response.Body = responseStream;

        var middleware = new ExceptionHandlingMiddleware(
            _ => throw exception,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        responseStream.Position = 0;
        using var reader = new StreamReader(responseStream);
        var rawBody = await reader.ReadToEndAsync();
        var body = JsonSerializer.Deserialize<BaseResult>(
            rawBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(body);
        return (context.Response.StatusCode, body!, rawBody);
    }

    private sealed class TestDbException(string message) : DbException(message)
    {
    }
}
