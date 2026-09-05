using Hangfire;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Notification;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class BackgroundNotificationJob
{
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;
    private readonly ILogger<BackgroundNotificationJob> _logger;

    public BackgroundNotificationJob(
        IEmailService emailService,
        ISmsService smsService,
        ILogger<BackgroundNotificationJob> logger)
    {
        _emailService = emailService;
        _smsService = smsService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task SendOtpAsync(
        string? firstName,
        string email,
        string? phoneNumber,
        string otp,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();

        try
        {
            await _emailService.SendOtpAsync(
                string.IsNullOrWhiteSpace(firstName) ? "Customer" : firstName,
                email,
                otp,
                cancellationToken);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            using var op = OperationLogger.Start(_logger, "BackgroundOtpEmail", ("Email", email));
            op.Fail("Background OTP email failed.", exception);
        }

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            try
            {
                await _smsService.SendOtpAsync(phoneNumber, otp, cancellationToken);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                using var op = OperationLogger.Start(_logger, "BackgroundOtpSms", ("PhoneNumber", phoneNumber));
                op.Fail("Background OTP SMS failed.", exception);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException("One or more OTP notifications failed.", failures);
    }

    [AutomaticRetry(Attempts = 3)]
    public Task SendOtpEmailAsync(
        string? firstName,
        string email,
        string otp,
        CancellationToken cancellationToken)
    {
        return _emailService.SendOtpAsync(
            string.IsNullOrWhiteSpace(firstName) ? "Customer" : firstName,
            email,
            otp,
            cancellationToken);
    }

    [AutomaticRetry(Attempts = 3)]
    public Task SendOtpSmsAsync(
        string phoneNumber,
        string otp,
        CancellationToken cancellationToken)
    {
        return _smsService.SendOtpAsync(phoneNumber, otp, cancellationToken);
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

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            try
            {
                await _smsService.SendOtpAsync(phoneNumber, otp, cancellationToken);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                using var op = OperationLogger.Start(_logger, "BackgroundPasswordResetSms", ("PhoneNumber", phoneNumber));
                op.Fail("Background password-reset SMS failed.", exception);
            }
        }

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
}
