using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mova.Infrastructure.Payment.Flutterwave;
using Mova.Infrastructure.Payment.Paystack;

namespace Mova.Infrastructure.Payment;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind Paystack Settings
        services.Configure<PaystackSettings>(
            configuration.GetSection(PaystackSettings.SectionName));

        services.Configure<FlutterwaveSettings>(
            configuration.GetSection(FlutterwaveSettings.SectionName));

        return services;
    }
        
}