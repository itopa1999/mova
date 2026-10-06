namespace Mova.Api.Security;

public sealed class PinDecryptionException : Exception
{
    public PinDecryptionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
