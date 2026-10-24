using Hangfire;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Jobs;

public sealed class NotificationQueue : INotificationQueue
{
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<NotificationQueue> _logger;

    public NotificationQueue(
        IBackgroundJobClient backgroundJobClient,
        ILogger<NotificationQueue> logger)
    {
        _backgroundJobClient = backgroundJobClient;
        _logger = logger;
    }

    public void QueueOtpDelivery(
        string? firstName,
        string email,
        string? phoneNumber,
        string otp,
        string purpose)
    {
        TryEnqueue(
            "QueueOtpDelivery.Email",
            () => _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
                job => job.SendOtpEmailAsync(
                    firstName, email, otp, purpose, CancellationToken.None)),
            ("Email", email));

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            TryEnqueue(
                "QueueOtpDelivery.Sms",
                () => _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
                    job => job.SendOtpSmsAsync(
                        phoneNumber, otp, purpose, CancellationToken.None)),
                ("Phone", phoneNumber));
        }
    }

    public void QueueWelcomeEmail(string firstName, string email)
    {
        TryEnqueue(
            "QueueWelcomeEmail",
            () => _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
                job => job.SendWelcomeEmailAsync(
                    firstName, email, CancellationToken.None)),
            ("Email", email));
    }

    public void QueueForgotPasswordOtp(
        string email,
        string? phoneNumber,
        string otp)
    {
        TryEnqueue(
            "QueueForgotPasswordOtp",
            () => _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
                job => job.SendForgotPasswordEmailAsync(
                    email, otp, CancellationToken.None)),
            ("Email", email));
    }

    public void QueueNotificationEmail(
        string firstName,
        string email,
        string message,
        string subject)
    {
        TryEnqueue(
            "QueueNotificationEmail",
            () => _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
                job => job.SendNotificationEmailAsync(
                    firstName, email, message, subject, CancellationToken.None)),
            ("Email", email),
            ("Subject", subject));
    }

    public void InAppNotificationAsync(
        string UserId,
        NotificationType Type,
        string Title,
        string Message,
        string? ActionUrl,
        string? Metadata,
        CancellationToken cancellationToken = default)
    {
        TryEnqueue(
            "InAppNotification",
            () => _backgroundJobClient.Enqueue<BackgroundNotificationJob>(
                job => job.InAppNotificationAsync(
                    UserId, Type, Title, Message, ActionUrl, Metadata,
                    CancellationToken.None)),
            ("UserId", UserId),
            ("Type", Type.ToString()));
    }

    public void QueueCreateVirtualAccount(
        string userPublicId,
        string firstName,
        string lastName,
        string email,
        string phoneNumber)
    {
        TryEnqueue(
            "QueueCreateVirtualAccount",
            () => _backgroundJobClient.Enqueue<BackgroundTaskJob>(
                job => job.CreateVirtualAccountAsync(
                    userPublicId, firstName, lastName, email, phoneNumber,
                    CancellationToken.None)),
            ("UserPublicId", userPublicId));
    }

    public void QueueCreateCircleWalletAsync(
        string UserPublicId,
        long CategoryId,
        long SavingsCircleId,
        string Name,
        string Description,
        decimal TargetAmount)
    {
        TryEnqueue(
            "QueueCreateVirtualAccount",
            () => _backgroundJobClient.Enqueue<BackgroundTaskJob>(
                job => job.CreateCircleWalletAsync(
                    UserPublicId, CategoryId, SavingsCircleId, Name, Description, TargetAmount,
                    CancellationToken.None)),
            ("UserPublicId", UserPublicId));
    }

    private void TryEnqueue(
        string operation,
        Action enqueue,
        params (string Key, object Value)[] context)
    {
        try
        {
            enqueue();
        }
        catch (Exception ex)
        {
            var props = new (string Key, object Value)[context.Length + 1];
            props[0] = ("Operation", operation);
            for (var i = 0; i < context.Length; i++)
            {
                props[i + 1] = context[i];
            }

            using var op = OperationLogger.Start(_logger, "NotificationEnqueue", props);
            op.Error("Failed to enqueue notification job.", ex);
        }
    }
}