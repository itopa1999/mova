using Hangfire;
using Mova.Application.Interfaces.Notification;

namespace Mova.Infrastructure.Jobs;

public sealed class NotificationQueue : INotificationQueue
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public NotificationQueue(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    public void QueueOtpDelivery(
        string? firstName,
        string email,
        string? phoneNumber,
        string otp)
    {
        _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
            job => job.SendOtpEmailAsync(
                firstName,
                email,
                otp,
                CancellationToken.None));

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
                job => job.SendOtpSmsAsync(
                    phoneNumber,
                    otp,
                    CancellationToken.None));
        }
    }

    public void QueueWelcomeEmail(string firstName, string email)
    {
        _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
            job => job.SendWelcomeEmailAsync(
                firstName,
                email,
                CancellationToken.None));
    }

    public void QueueForgotPasswordOtp(
        string email,
        string? phoneNumber,
        string otp)
    {
        _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
            job => job.SendForgotPasswordEmailAsync(
                email,
                otp,
                CancellationToken.None));

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
                job => job.SendOtpSmsAsync(
                    phoneNumber,
                    otp,
                    CancellationToken.None));
        }
    }
}
