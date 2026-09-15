using Mova.Domain.Enums;
using Mova.Shared.Constants;

namespace Mova.Application.Interfaces.Notification;

public interface INotificationQueue
{
    void QueueOtpDelivery(
        string? firstName,
        string email,
        string? phoneNumber,
        string otp,
        string purpose);

    void QueueForgotPasswordOtp(
        string email,
        string? phoneNumber,
        string otp);

    void QueueWelcomeEmail(
        string firstName,
        string email);

    void QueueNotificationEmail(
        string firstName,
        string email,
        string message,
        string subject);

    void InAppNotificationAsync(
        string UserId,
        NotificationType Type,
        string Title,
        string Message,
        string? ActionUrl,
        string? Metadata,
        CancellationToken cancellationToken = default
    );
}
