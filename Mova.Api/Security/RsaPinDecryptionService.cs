using System.Security.Cryptography;
using System.Text;

namespace Mova.Api.Security;

public sealed class RsaPinDecryptionService : IPinDecryptionService, IDisposable
{
    private readonly RSA _rsa;

    public RsaPinDecryptionService(IConfiguration configuration)
    {
        var configuredPrivateKey = configuration["PIN_ENCRYPTION_PRIVATE_KEY"];
        if (string.IsNullOrWhiteSpace(configuredPrivateKey))
        {
            throw new InvalidOperationException(
                "PIN_ENCRYPTION_PRIVATE_KEY is not configured.");
        }

        var rsa = RSA.Create();
        try
        {
            ImportPrivateKey(rsa, configuredPrivateKey);
        }
        catch (Exception exception) when (
            exception is CryptographicException
                or ArgumentException
                or FormatException
                or IOException
                or UnauthorizedAccessException)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                "PIN_ENCRYPTION_PRIVATE_KEY must contain a valid PEM private key, Base64-encoded PKCS#8 private key, or point to a readable private-key file.",
                exception);
        }

        _rsa = rsa;
    }

    public string Decrypt(string encryptedPin)
    {
        if (string.IsNullOrWhiteSpace(encryptedPin))
        {
            throw new PinDecryptionException("The encrypted PIN is required.");
        }

        byte[] ciphertext;
        try
        {
            ciphertext = Convert.FromBase64String(encryptedPin);
        }
        catch (FormatException exception)
        {
            throw new PinDecryptionException(
                "The encrypted PIN is not valid Base64.",
                exception);
        }

        if (ciphertext.Length != _rsa.KeySize / 8)
        {
            throw new PinDecryptionException(
                "The encrypted PIN has an invalid ciphertext length.");
        }

        byte[] plaintext;
        try
        {
            plaintext = _rsa.Decrypt(ciphertext, RSAEncryptionPadding.OaepSHA256);
        }
        catch (CryptographicException exception)
        {
            throw new PinDecryptionException(
                "The encrypted PIN could not be decrypted.",
                exception);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(plaintext);
        }
        catch (DecoderFallbackException exception)
        {
            throw new PinDecryptionException(
                "The decrypted PIN is not valid UTF-8.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Dispose() => _rsa.Dispose();

    private static void ImportPrivateKey(RSA rsa, string configuredPrivateKey)
    {
        var normalizedKey = configuredPrivateKey.Replace("\\n", "\n", StringComparison.Ordinal);
        if (normalizedKey.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            rsa.ImportFromPem(normalizedKey);
            return;
        }

        if (File.Exists(normalizedKey))
        {
            var privateKeyFile = File.ReadAllText(normalizedKey);
            rsa.ImportFromPem(privateKeyFile);
            return;
        }

        byte[] privateKeyBytes;
        try
        {
            privateKeyBytes = Convert.FromBase64String(normalizedKey);
        }
        catch (FormatException)
        {
            throw new ArgumentException(
                "The configured value is neither a PEM key, a readable file path, nor Base64-encoded key data.");
        }

        try
        {
            rsa.ImportPkcs8PrivateKey(privateKeyBytes, out var bytesRead);
            if (bytesRead != privateKeyBytes.Length)
            {
                throw new CryptographicException(
                    "The Base64-encoded private key contains trailing data.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }
}
