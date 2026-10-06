namespace Mova.Api.Security;

public interface IPinDecryptionService
{
    string Decrypt(string encryptedPin);
}
