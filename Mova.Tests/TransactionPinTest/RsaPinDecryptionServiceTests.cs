using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Mova.Api.Security;

namespace Mova.Tests.TransactionPinTest;

public sealed class RsaPinDecryptionServiceTests
{
    [Fact]
    public void Decrypt_ReturnsPlaintextEncryptedWithOaepSha256()
    {
        using var rsa = RSA.Create(2048);
        var configuration = CreateConfiguration(rsa.ExportPkcs8PrivateKeyPem());
        using var service = new RsaPinDecryptionService(configuration);
        var plaintext = Encoding.UTF8.GetBytes("042681");
        var encryptedPin = Convert.ToBase64String(
            rsa.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256));

        var result = service.Decrypt(encryptedPin);

        Assert.Equal("042681", result);
    }

    [Fact]
    public void Constructor_LoadsPrivateKeyFromFilePath()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
            using var service = new RsaPinDecryptionService(
                CreateConfiguration(privateKeyPath));
            var encryptedPin = Convert.ToBase64String(
                rsa.Encrypt(Encoding.UTF8.GetBytes("042681"), RSAEncryptionPadding.OaepSHA256));

            Assert.Equal("042681", service.Decrypt(encryptedPin));
        }
        finally
        {
            File.Delete(privateKeyPath);
        }
    }

    [Fact]
    public void Constructor_LoadsBase64EncodedPkcs8PrivateKey()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        using var service = new RsaPinDecryptionService(CreateConfiguration(privateKey));
        var encryptedPin = Convert.ToBase64String(
            rsa.Encrypt(Encoding.UTF8.GetBytes("042681"), RSAEncryptionPadding.OaepSHA256));

        Assert.Equal("042681", service.Decrypt(encryptedPin));
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("")]
    public void Decrypt_RejectsMalformedCiphertext(string encryptedPin)
    {
        using var rsa = RSA.Create(2048);
        using var service = new RsaPinDecryptionService(
            CreateConfiguration(rsa.ExportPkcs8PrivateKeyPem()));

        Assert.Throws<PinDecryptionException>(() => service.Decrypt(encryptedPin));
    }

    private static IConfiguration CreateConfiguration(string privateKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PIN_ENCRYPTION_PRIVATE_KEY"] = privateKey
            })
            .Build();
}
