using Microsoft.AspNetCore.Identity;
using Mova.Shared.Constants;

namespace Mova.Infrastructure.Identity;

public static class IdentitySeeder
{
    public static async Task SeedRolesAsync(
        RoleManager<IdentityRole<long>> roleManager)
    {
        var roles = new[]
        {
            Roles.Customer,
            Roles.SupportAgent,
            Roles.SuperAdmin,
            Roles.Admin
        };

        foreach (var role in roles)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            await roleManager.CreateAsync(
                new IdentityRole<long>
                {
                    Name = role
                });
        }
    }
}