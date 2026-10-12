namespace Mova.Application.Behaviors;

/// <summary>
/// Thrown when a PIN challenge could not be persisted after a
/// successful PIN verification. This is a system-side failure —
/// the caller's PIN was correct, so it must NOT be reported as
/// an invalid-PIN response.
/// </summary>
public sealed class PinChallengePersistenceException : Exception
{
    public PinChallengePersistenceException(string message)
        : base(message) { }

    public PinChallengePersistenceException(string message, Exception inner)
        : base(message, inner) { }
}