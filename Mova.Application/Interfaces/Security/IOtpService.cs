namespace Mova.Application.Interfaces.Security;

public record OtpResult(string RawOtp, string HashedOtp);

public interface IOtpService
{
    OtpResult GenerateSaltedOtp(string purpose);

    string Hash(string rawCode, string purpose);
}