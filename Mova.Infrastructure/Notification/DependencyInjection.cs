using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mova.Application.Interfaces.Notification;
using Mova.Infrastructure.Jobs;

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

        services.AddScoped<ISmsService, SmsService>();
        services.AddScoped<INotificationQueue, NotificationQueue>();

        return services;
    }
}