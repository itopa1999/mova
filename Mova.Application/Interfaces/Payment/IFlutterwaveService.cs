namespace Mova.Application.Interfaces.Payment;

public interface IFlutterwaveService
{
    Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBody,
        string signature);
}
