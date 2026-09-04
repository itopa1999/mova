namespace Mova.Application.Interfaces.Payment;

public interface IPaystackService
{
    Task<bool> VerifyWebhookSignatureAsync(
        string rawBody,
        string signature);
}