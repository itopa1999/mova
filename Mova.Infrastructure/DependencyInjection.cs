using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Application.Interfaces.Service;
using Mova.Infrastructure.Authentication.Jwt;
using Mova.Infrastructure.Caching;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Notification;
using Mova.Infrastructure.Notification.Email;
using Mova.Infrastructure.Payment;
using Mova.Infrastructure.Payment.Paystack;
using Mova.Infrastructure.Persistence;
using Mova.Infrastructure.Security;
using Mova.Infrastructure.Service;
using Mova.Infrastructure.Services;
using Mova.Infrastructure.Services.Security;
using StackExchange.Redis;

namespace Mova.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
    {
        services.AddIdentityServices(configuration);
        services.AddJwtAuthentication(configuration);
        services.AddPaymentServices(configuration);

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("The Redis connection string is not configured.");
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));


        services.AddScoped<ICacheService, RedisCacheService>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ITransactionPinService, TransactionPinService>();
        services.AddScoped<ISchedulePreviewService, SchedulePreviewService>();
        services.AddScoped<IWalletRuleValidator, WalletRuleValidator>();
        services.AddScoped<IPaystackService, PaystackService>();
        services.AddNotificationServices(configuration);
        services.AddSingleton<TemplateRenderer>();
        
        return services;
    }
}
