using Hangfire;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class BackgroundNotificationJob
{
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;
    private readonly ILogger<BackgroundNotificationJob> _logger;
    private readonly ApplicationDbContext _context;

    public BackgroundNotificationJob(
        IEmailService emailService,
        ISmsService smsService,
        ILogger<BackgroundNotificationJob> logger,
        ApplicationDbContext context)
    {
        _emailService = emailService;
        _smsService = smsService;
        _logger = logger;
        _context = context;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task SendOtpAsync(
        string? firstName,
        string email,
        string? phoneNumber,
        string otp,
        string purpose,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();

        try
        {
            await _emailService.SendOtpAsync(
                string.IsNullOrWhiteSpace(firstName) ? "Customer" : firstName,
                email,
                otp,
                purpose,
                cancellationToken);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            using var op = OperationLogger.Start(_logger, "BackgroundOtpEmail", ("Email", email));
            op.Fail("Background OTP email failed.", exception);
        }

        // if (!string.IsNullOrWhiteSpace(phoneNumber))
        // {
        //     try
        //     {
        //         await _smsService.SendOtpAsync(phoneNumber, otp, cancellationToken);
        //     }
        //     catch (Exception exception)
        //     {
        //         failures.Add(exception);
        //         using var op = OperationLogger.Start(_logger, "BackgroundOtpSms", ("PhoneNumber", phoneNumber));
        //         op.Fail("Background OTP SMS failed.", exception);
        //     }
        // }

        if (failures.Count > 0)
            throw new AggregateException("One or more OTP notifications failed.", failures);
    }

    [AutomaticRetry(Attempts = 3)]
    public Task SendOtpEmailAsync(
        string? firstName,
        string email,
        string otp,
        string purpose,
        CancellationToken cancellationToken)
    {
        return _emailService.SendOtpAsync(
            string.IsNullOrWhiteSpace(firstName) ? "Customer" : firstName,
            email,
            otp,
            purpose,
            cancellationToken);
    }

    [AutomaticRetry(Attempts = 3)]
    public Task SendOtpSmsAsync(
        string phoneNumber,
        string otp,
        string purpose,
        CancellationToken cancellationToken)
    {
        return _smsService.SendOtpAsync(phoneNumber, otp, purpose, cancellationToken);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task SendWelcomeEmailAsync(
        string firstName,
        string email,
        CancellationToken cancellationToken)
    {
        await _emailService.SendWelcomeEmailAsync(
            string.IsNullOrWhiteSpace(firstName) ? "Customer" : firstName,
            email,
            cancellationToken);
    }


    [AutomaticRetry(Attempts = 3)]
    public async Task SendNotificationEmailAsync(
        string firstName,
        string email,
        string message,
        string subject,
        CancellationToken cancellationToken)
    {
        await _emailService.SendNotificationEmailAsync(
            string.IsNullOrWhiteSpace(firstName) ? "Customer" : firstName,
            email,
            message,
            subject,
            cancellationToken);
    }


    [AutomaticRetry(Attempts = 3)]
    public async Task SendForgotPasswordOtpAsync(
        string email,
        string? phoneNumber,
        string otp,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();

        try
        {
            await _emailService.SendForgotPasswordOtpAsync(email, otp, cancellationToken);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            using var op = OperationLogger.Start(_logger, "BackgroundPasswordResetEmail", ("Email", email));
            op.Fail("Background password-reset email failed.", exception);
        }

        // if (!string.IsNullOrWhiteSpace(phoneNumber))
        // {
        //     try
        //     {
        //         await _smsService.SendOtpAsync(phoneNumber, otp, purpose, cancellationToken);
        //     }
        //     catch (Exception exception)
        //     {
        //         failures.Add(exception);
        //         using var op = OperationLogger.Start(_logger, "BackgroundPasswordResetSms", ("PhoneNumber", phoneNumber));
        //         op.Fail("Background password-reset SMS failed.", exception);
        //     }
        // }

        if (failures.Count > 0)
            throw new AggregateException("One or more password-reset notifications failed.", failures);
    }

    [AutomaticRetry(Attempts = 3)]
    public Task SendForgotPasswordEmailAsync(
        string email,
        string otp,
        CancellationToken cancellationToken)
    {
        return _emailService.SendForgotPasswordOtpAsync(email, otp, cancellationToken);
    }

    [AutomaticRetry(Attempts = 2)]
    public async Task InAppNotificationAsync(
        string userId,
        NotificationType type,
        string title,
        string message,
        string? actionUrl,
        string? metadata,
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(
            _logger,
            "BackgroundInAppNotification",
            ("UserId", userId),
            ("Type", type.ToString()));

        try
        {
            var notification = new AppNotification
            {
                UserPublicId = userId,
                Type = type,
                Title = title.Trim(),
                Message = message.Trim(),
                IsRead = false,
                ActionUrl = actionUrl,
                Metadata = metadata,
            };

            await _context.Set<AppNotification>()
                .AddAsync(notification, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            op.Success($"In-app notification saved. Id: {notification.Id}");
        }
        catch (Exception exception)
        {
            op.Fail("Background in-app notification failed.", exception);
            throw;
        }
    }
}
