namespace Mova.Application.Interfaces.Security;

public enum TransactionPinVerificationResult
{
    Verified,
    Invalid,
    Locked,
    LockedNow
}
