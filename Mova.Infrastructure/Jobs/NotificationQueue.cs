using Hangfire;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Enums;

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
        string otp,
        string purpose)
    {
        _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
            job => job.SendOtpEmailAsync(
                firstName,
                email,
                otp,
                purpose,
                CancellationToken.None));

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
                job => job.SendOtpSmsAsync(
                    phoneNumber,
                    otp,
                    purpose,
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

        // if (!string.IsNullOrWhiteSpace(phoneNumber))
        // {
        //     _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
        //         job => job.SendOtpSmsAsync(
        //             phoneNumber,
        //             otp,
        //             CancellationToken.None));
        // }
    }

    public void QueueNotificationEmail(string firstName, string email, string message, string subject)
    {
        _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
            job => job.SendNotificationEmailAsync(
                firstName,
                email,
                message,
                subject,
                CancellationToken.None));
    }

    public void InAppNotificationAsync(string UserId, NotificationType Type, string Title, 
    string Message, string? ActionUrl, string? Metadata, 
    CancellationToken cancellationToken = default)
    {
        _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
            job => job.InAppNotificationAsync(
                UserId,
                Type,
                Title,
                Message,
                ActionUrl,
                Metadata,
                CancellationToken.None));
    }
}
