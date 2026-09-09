
namespace Mova.Application.Interfaces.Notification;
public interface ISmsService
{
    Task SendOtpAsync(
        string phoneNumber,
        string otp,
        string purpose,
        CancellationToken cancellationToken = default);
}
