using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Persistence;
using Mova.Infrastructure.Persistence.Seeding;
using Mova.Shared.Constants;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class DatabaseSeederTests : BaseTest
{
    private readonly Mock<IBankService> _bankService = new();

    private (
        DatabaseSeeder Seeder,
        UserManager<User> UserManager,
        RoleManager<IdentityRole<long>> RoleManager)
        BuildSeeder()
    {
        var services = new ServiceCollection();

        services.AddScoped<ApplicationDbContext>(_ => Context);

        services
            .AddIdentityCore<User>(options =>
            {
                options.Password.RequiredLength = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = false;
                options.Lockout.AllowedForNewUsers = false;
            })
            .AddRoles<IdentityRole<long>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<User>>();

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<long>>>();

        var seeder = new DatabaseSeeder(
            Context,
            userManager,
            roleManager,
            _bankService.Object,
            NullLogger<DatabaseSeeder>.Instance);

        return (seeder, userManager, roleManager);
    }

    private void SetupBankRefreshInsertsBanks(
        string name = "Seeded Bank",
        string code = "001")
    {
        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                Context.Banks.Add(new Bank
                {
                    Name = name,
                    Code = code,
                    IsActive = true,
                });
                await Context.SaveChangesAsync(ct);
            });
    }

    // ─── Full SeedAsync ───────────────────────────────

    [Fact]
    public async Task SeedAsync_OnFreshDatabase_SeedsEverything()
    {
        var (seeder, userManager, _) = BuildSeeder();
        SetupBankRefreshInsertsBanks();

        await seeder.SeedAsync();

        Assert.True(await Context.WalletCategories.AnyAsync());
        Assert.True(await Context.FeatureFlags.AnyAsync());
        Assert.True(await Context.WalletTemplates.AnyAsync());
        Assert.True(await Context.Banks.AnyAsync());

        var admin = await userManager.FindByEmailAsync("admin@mova.com");
        Assert.NotNull(admin);

        _bankService.Verify(
            x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_WhenCalledTwice()
    {
        var (seeder, _, _) = BuildSeeder();
        SetupBankRefreshInsertsBanks();

        await seeder.SeedAsync();

        var categoriesAfterFirst = await Context.WalletCategories.CountAsync();
        var flagsAfterFirst = await Context.FeatureFlags.CountAsync();
        var templatesAfterFirst = await Context.WalletTemplates.CountAsync();
        var banksAfterFirst = await Context.Banks.CountAsync();

        await seeder.SeedAsync();

        Assert.Equal(categoriesAfterFirst, await Context.WalletCategories.CountAsync());
        Assert.Equal(flagsAfterFirst, await Context.FeatureFlags.CountAsync());
        Assert.Equal(templatesAfterFirst, await Context.WalletTemplates.CountAsync());
        Assert.Equal(banksAfterFirst, await Context.Banks.CountAsync());

        // Second pass sees banks already present → does not refresh.
        _bankService.Verify(
            x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─── Wallet categories ────────────────────────────

    [Fact]
    public async Task SeedAsync_InsertsDefaultWalletCategories()
    {
        var (seeder, _, _) = BuildSeeder();

        await seeder.SeedAsync();

        var expected = DefaultWalletCategories.Create().Count;
        Assert.Equal(expected, await Context.WalletCategories.CountAsync());
    }

    // ─── Feature flags ────────────────────────────────

    [Fact]
    public async Task SeedAsync_InsertsDefaultFeatureFlags()
    {
        var (seeder, _, _) = BuildSeeder();

        await seeder.SeedAsync();

        var expected = DefaultFeatureFlags.Create().Count;
        Assert.Equal(expected, await Context.FeatureFlags.CountAsync());
    }

    // ─── Wallet templates ─────────────────────────────

    [Fact]
    public async Task SeedAsync_InsertsDefaultWalletTemplates()
    {
        var (seeder, _, _) = BuildSeeder();

        await seeder.SeedAsync();

        var expected = DefaultWalletTemplates.Create().Count;
        Assert.Equal(expected, await Context.WalletTemplates.CountAsync());
    }

    // ─── Admin user ───────────────────────────────────

    [Fact]
    public async Task SeedAsync_CreatesAdminWithExpectedIdentity()
    {
        var (seeder, userManager, _) = BuildSeeder();

        await seeder.SeedAsync();

        var admin = await userManager.FindByEmailAsync("admin@mova.com");

        Assert.NotNull(admin);
        Assert.Equal("admin@mova.com", admin!.Email);
        Assert.Equal("MovaAdmin1", admin.PublicId);
        Assert.Equal("Mova", admin.FirstName);
        Assert.Equal("Admin", admin.LastName);
    }

    [Fact]
    public async Task SeedAsync_AdminHasBothSuperAdminAndAdminRoles()
    {
        var (seeder, userManager, _) = BuildSeeder();

        await seeder.SeedAsync();

        var admin = await userManager.FindByEmailAsync("admin@mova.com");
        var roles = await userManager.GetRolesAsync(admin!);

        Assert.Contains(Roles.SuperAdmin, roles);
        Assert.Contains(Roles.Admin, roles);
    }

    [Fact]
    public async Task SeedAsync_AdminHasNoPasswordHash()
    {
        var (seeder, userManager, _) = BuildSeeder();

        await seeder.SeedAsync();

        var admin = await userManager.FindByEmailAsync("admin@mova.com");

        Assert.True(string.IsNullOrEmpty(admin!.PasswordHash));
    }

    [Fact]
    public async Task SeedAsync_AdminIsEmailAndPhoneVerified()
    {
        var (seeder, userManager, _) = BuildSeeder();

        await seeder.SeedAsync();

        var admin = await userManager.FindByEmailAsync("admin@mova.com");

        Assert.True(admin!.EmailConfirmed);
        Assert.True(admin.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task SeedAsync_AdminStartsWithZeroBalance()
    {
        var (seeder, userManager, _) = BuildSeeder();

        await seeder.SeedAsync();

        var admin = await userManager.FindByEmailAsync("admin@mova.com");

        Assert.Equal(0m, admin!.Balance.ToDecimal());
    }

    [Fact]
    public async Task SeedAsync_WhenAdminAlreadyExists_DoesNotDuplicate()
    {
        var (seeder, userManager, _) = BuildSeeder();

        await seeder.SeedAsync();
        var firstAdmin = await userManager.FindByEmailAsync("admin@mova.com");

        await seeder.SeedAsync();

        var usersWithEmail = await Context.Users
            .Where(u => u.Email == "admin@mova.com")
            .ToListAsync();

        Assert.Single(usersWithEmail);
        Assert.Equal(firstAdmin!.Id, usersWithEmail[0].Id);
    }

    [Fact]
    public async Task SeedAsync_EnsuresAllRolesExist()
    {
        var (seeder, _, roleManager) = BuildSeeder();

        await seeder.SeedAsync();

        Assert.True(await roleManager.RoleExistsAsync(Roles.SuperAdmin));
        Assert.True(await roleManager.RoleExistsAsync(Roles.Admin));
        Assert.True(await roleManager.RoleExistsAsync(Roles.Customer));
        Assert.True(await roleManager.RoleExistsAsync(Roles.SupportAgent));
    }

    [Fact]
    public async Task SeedAsync_WhenAdminExists_StillEnsuresRolesExist()
    {
        var (seeder, _, roleManager) = BuildSeeder();

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.True(await roleManager.RoleExistsAsync(Roles.SuperAdmin));
        Assert.True(await roleManager.RoleExistsAsync(Roles.Admin));
    }

    // ─── Banks ────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_WithNoBanks_CallsRefresh()
    {
        var (seeder, _, _) = BuildSeeder();
        SetupBankRefreshInsertsBanks();

        await seeder.SeedAsync();

        _bankService.Verify(
            x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SeedAsync_WithExistingBanks_SkipsRefresh()
    {
        var (seeder, _, _) = BuildSeeder();

        Context.Banks.Add(new Bank
        {
            Name = "Test Bank",
            Code = "999",
            IsActive = true,
        });
        await Context.SaveChangesAsync();

        await seeder.SeedAsync();

        _bankService.Verify(
            x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SeedAsync_WhenBankRefreshThrows_DoesNotRethrow()
    {
        var (seeder, _, _) = BuildSeeder();

        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated bank API failure"));

        await seeder.SeedAsync();
    }

    [Fact]
    public async Task SeedAsync_WhenBankRefreshThrows_StillSeedsEverythingElse()
    {
        var (seeder, userManager, _) = BuildSeeder();

        _bankService
            .Setup(x => x.RefreshBanksAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated bank API failure"));

        await seeder.SeedAsync();

        Assert.True(await Context.WalletCategories.AnyAsync());
        Assert.True(await Context.FeatureFlags.AnyAsync());
        Assert.True(await Context.WalletTemplates.AnyAsync());

        var admin = await userManager.FindByEmailAsync("admin@mova.com");
        Assert.NotNull(admin);
    }

    [Fact]
    public async Task SeedAsync_WhenBankRefreshReturnsNoBanks_DoesNotThrow()
    {
        var (seeder, _, _) = BuildSeeder();

        // Mock does nothing — mimics the real service returning early on empty provider response.
        await seeder.SeedAsync();

        Assert.False(await Context.Banks.AnyAsync());
    }
}