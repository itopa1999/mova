using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Mova.Shared.Logging;

/// <summary>
/// Structured logger for tracking the lifecycle of an operation.
///
/// Log levels:
///   Success → Information (default log sink, not sent to Sentry)
///   Warning → Warning     (business rejections — not sent to Sentry)
///   Error   → Error       (system failures — sent to Sentry + email alert)
/// </summary>
public class OperationLogger : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _operationName;
    private readonly IDisposable? _scope;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    public OperationLogger(
        ILogger logger,
        string operationName,
        params (string Key, object Value)[] properties)
    {
        _logger = logger;
        _operationName = operationName;
        _stopwatch = Stopwatch.StartNew();

        var scopeProps = new Dictionary<string, object>
        {
            ["Operation"] = operationName,
            ["StartTime"] = DateTimeOffset.UtcNow.ToString("O")
        };

        foreach (var (key, value) in properties)
        {
            scopeProps[key] = value;
        }

        _scope = _logger.BeginScope(scopeProps);

        _logger.LogInformation("Starting operation. {@Properties}", scopeProps);
    }

    public static OperationLogger Start(
        ILogger logger,
        string operationName,
        params (string Key, object Value)[] properties)
        => new(logger, operationName, properties);

    /// <summary>
    /// Operation completed successfully. Logged at Information.
    /// </summary>
    public void Success(string message = "Operation completed successfully")
    {
        _stopwatch.Stop();

        _logger.LogInformation(
            "{Message} in {Elapsed:F2}s",
            message,
            _stopwatch.Elapsed.TotalSeconds);

        DisposeScope();
    }

    /// <summary>
    /// Business-level warning — validation errors, not-found, business rules,
    /// invalid credentials, insufficient balance, etc.
    /// Logged at Warning. Not sent to Sentry.
    /// </summary>
    public void Warning(string message = "Operation warning")
    {
        _stopwatch.Stop();

        _logger.LogWarning(
            "{Message} in {Elapsed:F2}s",
            message,
            _stopwatch.Elapsed.TotalSeconds);

        DisposeScope();
    }

    /// <summary>
    /// System-level error with an exception — infrastructure failures,
    /// programming bugs, unhandled exceptions.
    /// Logged at Error. Sent to Sentry + email alert.
    /// </summary>
    public void Error(string message, Exception exception)
    {
        _stopwatch.Stop();

        _logger.LogError(
            exception,
            "{Message} after {Elapsed:F2}s",
            message,
            _stopwatch.Elapsed.TotalSeconds);

        DisposeScope();
    }

    /// <summary>
    /// System-level error without an exception.
    /// Logged at Error. Sent to Sentry + email alert.
    /// </summary>
    public void Error(string message)
    {
        _stopwatch.Stop();

        _logger.LogError(
            "{Message} after {Elapsed:F2}s",
            message,
            _stopwatch.Elapsed.TotalSeconds);

        DisposeScope();
    }

    private void DisposeScope()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _scope?.Dispose();
        _stopwatch.Stop();
    }

    public void Dispose() => DisposeScope();
}