using System.Security.Cryptography;
using Mova.Application.Interfaces.Security;
namespace Mova.Infrastructure.Security;
public sealed class OtpService : IOtpService
{
    private readonly SensitiveIdentifierHasher _hasher;

    public OtpService(SensitiveIdentifierHasher hasher)
    {
        _hasher = hasher;
    }

    public OtpResult GenerateSaltedOtp(string purpose)
    {
        var rawOtp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        var hashedOtp = _hasher.Hash(rawOtp, purpose);

        return new OtpResult(rawOtp, hashedOtp);
    }

    public string Hash(string rawCode, string purpose)
    {
        return _hasher.Hash(rawCode, purpose);
    }

}