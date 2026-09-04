
namespace Mova.Application.Interfaces.Notification;
public interface ISmsService
{
    Task SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken cancellationToken = default);
}
