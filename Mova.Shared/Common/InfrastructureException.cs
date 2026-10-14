namespace Mova.Shared.Common;

/// <summary>
/// Signals a system-level failure that has nothing to do with the caller.
/// Middleware maps this to 500 and logs at Error (Sentry + email alert).
/// Use only when the request is legitimate but the system couldn't complete it.
/// </summary>
public sealed class InfrastructureException : Exception
{
    public InfrastructureException(string message) : base(message) { }

    public InfrastructureException(string message, Exception inner)
        : base(message, inner) { }
}