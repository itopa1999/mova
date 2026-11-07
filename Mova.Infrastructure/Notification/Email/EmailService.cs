using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Mova.Application.Models;
using Mova.Application.Interfaces.Notification;
using Mova.Shared.Constants;
using Mova.Shared.Logging;
using Mova.Application.ExternalAPI;
namespace Mova.Infrastructure.Notification.Email;

public sealed class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ExternalApiSettings _externalApiSettings;
    private readonly ILogger<EmailService> _logger;

    private readonly TemplateRenderer _renderer;

    public EmailService(
        IOptions<EmailSettings> options,
        IOptions<ExternalApiSettings> externalApiOptions,
        ILogger<EmailService> logger,
        TemplateRenderer renderer)
    {
        _settings = options.Value;
        _externalApiSettings = externalApiOptions.Value;
        _logger = logger;
        _renderer = renderer;
    }

    public async Task SendOtpAsync(
        string name,
        string email,
        string otp,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        var message = purpose switch
        {
            OtpPurpose.AccountVerification =>
                "Use the one-time password below to verify your MOVA account.",

            OtpPurpose.PasswordReset =>
                "Use the one-time password below to reset your MOVA account password.",

            _ =>
                "Use the one-time password below to complete your request."
        };
        var body = await _renderer.RenderAsync(
            "OtpEmailTemplate.html",
            new Dictionary<string, string>
            {
                ["Name"] = name,
                ["OTP"] = otp,
                ["Expiry"] = "2 Minutes",
                ["Message"] = message
            }, cancellationToken);

        await SendEmailAsync(
            email,
            "Mova Verification Code",
            body,
            cancellationToken);
    }

    public async Task SendForgotPasswordOtpAsync(
        string email,
        string otp,
        CancellationToken cancellationToken = default)
    {
        var body = await _renderer.RenderAsync(
            "ForgotPasswordOtpEmailTemplate.html",
            new Dictionary<string, string>
            {
                ["Name"] = "Customer",
                ["OTP"] = otp,
                ["Expiry"] = "2 Minutes"
            }, cancellationToken);

        await SendEmailAsync(
            email,
            "Mova Forgot Password Verification Code",
            body,
            cancellationToken);
    }

    public async Task SendWelcomeEmailAsync(
        string firstName,
        string email,
        CancellationToken cancellationToken = default)
    {
        var dashboardUrl = string.IsNullOrWhiteSpace(_externalApiSettings.FrontendBaseUrl)
            ? "https://app.mova.app"
            : _externalApiSettings.FrontendBaseUrl.TrimEnd('/');

        var body = await _renderer.RenderAsync(
            "WelcomeEmailTemplate.html",
            new Dictionary<string, string>
            {
                ["FirstName"] = firstName,
                ["DashboardUrl"] = dashboardUrl
            }, cancellationToken);

        await SendEmailAsync(
            email,
            "Welcome to Mova",
            body,
            cancellationToken);
    }

    public async Task SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        await SendAsync(
            new EmailMessage
            {
                To = toEmail,
                Subject = subject,
                Body = htmlBody,
                IsHtml = true
            },
            cancellationToken);
    }

    public async Task SendAsync(
        EmailMessage email,
        CancellationToken cancellationToken = default)
    {
        using var op = OperationLogger.Start(
            _logger,
            "SendEmail",
            ("Recipient", email.To),
            ("Subject", email.Subject));

        try
        {
            var message = new MimeMessage();

            //----------------------------------------------------
            // FROM
            //----------------------------------------------------
            message.From.Add(
                new MailboxAddress(
                    _settings.FromName,
                    _settings.FromEmail));

            //----------------------------------------------------
            // TO
            //----------------------------------------------------
            message.To.Add(
                MailboxAddress.Parse(email.To));

            //----------------------------------------------------
            // CC
            //----------------------------------------------------
            foreach (var cc in email.Cc)
            {
                if (!string.IsNullOrWhiteSpace(cc))
                {
                    message.Cc.Add(
                        MailboxAddress.Parse(cc));
                }
            }

            //----------------------------------------------------
            // BCC
            //----------------------------------------------------
            foreach (var bcc in email.Bcc)
            {
                if (!string.IsNullOrWhiteSpace(bcc))
                {
                    message.Bcc.Add(
                        MailboxAddress.Parse(bcc));
                }
            }

            //----------------------------------------------------
            // SUBJECT
            //----------------------------------------------------
            message.Subject = email.Subject;

            //----------------------------------------------------
            // BODY
            //----------------------------------------------------
            var bodyBuilder = new BodyBuilder();

            if (email.IsHtml)
            {
                bodyBuilder.HtmlBody = email.Body;
            }
            else
            {
                bodyBuilder.TextBody = email.Body;
            }

            //----------------------------------------------------
            // ATTACHMENTS
            //----------------------------------------------------
            foreach (var attachment in email.Attachments)
            {
                bodyBuilder.Attachments.Add(
                    attachment.FileName,
                    attachment.Data,
                    ContentType.Parse(attachment.ContentType));
            }

            message.Body = bodyBuilder.ToMessageBody();

            //----------------------------------------------------
            // SMTP
            //----------------------------------------------------
            using var smtp = new SmtpClient();

            var security = _settings.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await smtp.ConnectAsync(
                _settings.Host,
                _settings.Port,
                security,
                cancellationToken);

            await smtp.AuthenticateAsync(
                _settings.Username,
                _settings.Password,
                cancellationToken);

            await smtp.SendAsync(
                message,
                cancellationToken);

            await smtp.DisconnectAsync(
                true,
                cancellationToken);

            op.Success($"Email sent successfully to {email.To}");
        }
        catch (Exception ex)
        {
            op.Error($"Failed to send email to {email.To}", ex);

            throw;
        }
    }

    public async Task SendNotificationEmailAsync(string firstName, string email, string message, string subject, CancellationToken cancellationToken = default)
    {
        var body = await _renderer.RenderAsync(
            "NotificationEmailTemplate.html",
            new Dictionary<string, string>
            {
                ["FirstName"] = firstName,
                ["Message"] = message
            }, cancellationToken);

        await SendEmailAsync(
            email,
            subject,
            body,
            cancellationToken);
    }
}