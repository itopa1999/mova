

using Mova.Application.Common.Models;

namespace Mova.Application.Interfaces.Notification;
public interface IEmailService
{
    Task SendOtpAsync(
        string firstName,
        string email,
        string otp,
        CancellationToken cancellationToken = default
    );

    Task SendForgotPasswordOtpAsync(
        string email,
        string otp,
        CancellationToken cancellationToken = default
    );

    Task SendWelcomeEmailAsync(
        string firstName,
        string email,
        CancellationToken cancellationToken = default
    );

    Task SendAsync(
        EmailMessage email,
        CancellationToken cancellationToken = default
    );

    Task SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default
    );
}
