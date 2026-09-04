using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Mova.Infrastructure.Identity;

public static class IdentityExtensions
{
    public static async Task SeedIdentityAsync(
        this IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<long>>>();

        await IdentitySeeder.SeedRolesAsync(roleManager);
    }
}