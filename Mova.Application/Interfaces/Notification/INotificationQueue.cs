using Mova.Domain.Enums;

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

    void QueueCreateVirtualAccount(
        string userPublicId,
        string firstName,
        string lastName,
        string email,
        string phoneNumber
    );
}
