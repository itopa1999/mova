using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Mova.Shared.Logging;

/// <summary>
/// Structured logger for tracking the lifecycle of an operation.
/// Use with ILogger and BeginScope to attach properties to all log entries.
/// </summary>
public class OperationLogger : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _operationName;
    private readonly IDisposable? _scope;
    private readonly Stopwatch _stopwatch;

    /// <summary>
    /// Creates a new OperationLogger instance.
    /// </summary>
    /// <param name="logger">ILogger instance (injected via DI).</param>
    /// <param name="operationName">Name of the operation (e.g., "RegisterUser").</param>
    /// <param name="properties">Additional key-value pairs to attach to logs.</param>
    public OperationLogger(ILogger logger, string operationName, params (string Key, object Value)[] properties)
    {
        _logger = logger;
        _operationName = operationName;
        _stopwatch = Stopwatch.StartNew();

        // Attach properties to the log scope
        var scopeProps = new Dictionary<string, object>
        {
            ["Operation"] = operationName,
            ["StartTime"] = DateTimeOffset.UtcNow.ToString("O")
        };
        foreach (var (key, value) in properties)
            scopeProps[key] = value;

        _scope = _logger.BeginScope(scopeProps);

        // Log the start
        _logger.LogInformation("Starting operation. {@Properties}", scopeProps);
    }

    public static OperationLogger Start(ILogger logger, string operationName, params (string Key, object Value)[] properties)
    {
        return new OperationLogger(logger, operationName, properties);
    }

    /// <summary>
    /// Logs a success message with elapsed time.
    /// </summary>
    public void Success(string message = "Operation completed successfully")
    {
        _stopwatch.Stop();
        _logger.LogInformation("{Message} in {Elapsed:F2}s", message, _stopwatch.Elapsed.TotalSeconds);
        DisposeScope();
    }

    /// <summary>
    /// Logs a failure message with elapsed time and optional exception details.
    /// </summary>
    public void Fail(string message = "Operation failed", Exception? exception = null)
    {
        _stopwatch.Stop();
        if (exception != null)
        {
            _logger.LogError(exception, "{Message} after {Elapsed:F2}s", message, _stopwatch.Elapsed.TotalSeconds);
        }
        else
        {
            _logger.LogError("{Message} after {Elapsed:F2}s", message, _stopwatch.Elapsed.TotalSeconds);
        }
        DisposeScope();
    }

    private void DisposeScope()
    {
        _scope?.Dispose();
        _stopwatch.Stop();
    }

    public void Dispose()
    {
        DisposeScope();
    }
}