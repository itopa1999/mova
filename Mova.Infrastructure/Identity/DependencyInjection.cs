using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mova.Application.Interfaces.Identity;
using Mova.Infrastructure.Persistence;

namespace Mova.Infrastructure.Identity;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IIdentityService, IdentityService>();
        
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var databaseProvider = configuration
                .GetValue<string>("DatabaseProvider")?
                .Trim()
                .ToLowerInvariant();

            if (databaseProvider is "postgres"
                or "postgresql"
                or "npgsql")
            {
                var connectionString =
                    configuration.GetConnectionString("Postgres")
                    ?? throw new InvalidOperationException(
                        "Postgres connection string is missing.");

                options.UseNpgsql(connectionString);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported database provider: {databaseProvider}");
            }
        });

        services
            .AddIdentity<User, IdentityRole<long>>(options =>
            {
                ConfigurePassword(options);
                ConfigureUser(options);
                ConfigureLockout(options);
                ConfigureSignIn(options);
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    private static void ConfigurePassword(IdentityOptions options)
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
    }

    private static void ConfigureUser(IdentityOptions options)
    {
        options.User.RequireUniqueEmail = true;
    }

    private static void ConfigureLockout(IdentityOptions options)
    {
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    }

    private static void ConfigureSignIn(IdentityOptions options)
    {
        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedPhoneNumber = false;
    }
}