using Mova.Application.Interfaces.Notification;

namespace Mova.Infrastructure.Notification;
public sealed class SmsService : ISmsService
{
    public Task SendOtpAsync(
        string phoneNumber,
        string otp,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine(
            $"SMS OTP -> {phoneNumber} : {otp}");

        return Task.CompletedTask;
    }
}
