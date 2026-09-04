using Mova.Application.Interfaces.Notification;

namespace Mova.Infrastructure.Notification;
public sealed class SmsService : ISmsService
{
    public Task SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine(
            $"SMS OTP -> {phoneNumber} : {otp}");

        return Task.CompletedTask;
    }
}
