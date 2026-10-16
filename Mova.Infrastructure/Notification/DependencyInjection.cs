using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mova.Application.Interfaces.Notification;
using Mova.Infrastructure.Jobs;
using Mova.Infrastructure.Notification.Sms;

namespace Mova.Infrastructure.Notification;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailSettings>(
            configuration.GetSection(EmailSettings.SectionName));

        services.AddScoped<IEmailService, EmailService>();

        services.Configure<TermiiSettings>(
            configuration.GetSection(TermiiSettings.SectionName));

        services.AddHttpClient("Termii", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddScoped<ISmsService, SmsService>();
        services.AddScoped<INotificationQueue, NotificationQueue>();

        return services;
    }
}