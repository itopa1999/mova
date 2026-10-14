using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Identity;
using Mova.Shared.Constants;
using Mova.Shared.Logging;

namespace Mova.Infrastructure.Persistence.Seeding;

public sealed class DatabaseSeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole<long>> _roleManager;
    private readonly IBankService _bankService;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        ApplicationDbContext dbContext,
        UserManager<User> userManager,
        RoleManager<IdentityRole<long>> roleManager,
        IBankService bankService,
        ILogger<DatabaseSeeder> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _bankService = bankService;
        _logger = logger;
    }

    public async Task SeedAsync(
        CancellationToken cancellationToken = default)
    {
        await SeedWalletCategoriesAsync(cancellationToken);
        await SeedFeatureFlagsAsync(cancellationToken);
        await SeedWalletTemplatesAsync(cancellationToken);
        await SeedAdminUserAsync(cancellationToken);
        await SeedBanksIfMissingAsync(cancellationToken);
    }

    private async Task SeedWalletCategoriesAsync(
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "SeedWalletCategories");

        var existingCategoryIds = (await _dbContext.WalletCategories
            .Select(x => x.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var defaultCategories = DefaultWalletCategories.Create();

        var categoriesToAdd = defaultCategories
            .Where(x => !existingCategoryIds.Contains(x.Id))
            .ToList();

        if (categoriesToAdd.Count == 0)
        {
            op.Success("Wallet categories already seeded — nothing to do.");
            return;
        }

        await _dbContext.WalletCategories.AddRangeAsync(
            categoriesToAdd,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        op.Success($"Seeded {categoriesToAdd.Count} wallet category(ies).");
    }

    private async Task SeedFeatureFlagsAsync(
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "SeedFeatureFlags");

        var existingNames = (await _dbContext.FeatureFlags
            .Select(x => x.Name)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var defaultFlags = DefaultFeatureFlags.Create();

        var flagsToAdd = defaultFlags
            .Where(x => !existingNames.Contains(x.Name))
            .ToList();

        if (flagsToAdd.Count == 0)
        {
            op.Success("Feature flags already seeded — nothing to do.");
            return;
        }

        await _dbContext.FeatureFlags.AddRangeAsync(
            flagsToAdd,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        op.Success($"Seeded {flagsToAdd.Count} feature flag(s).");
    }

    private async Task SeedWalletTemplatesAsync(
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "SeedWalletTemplates");

        var existingNames = (await _dbContext.WalletTemplates
            .Select(x => x.Name)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var defaultTemplates = DefaultWalletTemplates.Create();

        var templatesToAdd = defaultTemplates
            .Where(x => !existingNames.Contains(x.Name))
            .ToList();

        if (templatesToAdd.Count == 0)
        {
            op.Success("Wallet templates already seeded — nothing to do.");
            return;
        }

        await _dbContext.WalletTemplates.AddRangeAsync(
            templatesToAdd,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        op.Success($"Seeded {templatesToAdd.Count} wallet template(s).");
    }

    private async Task SeedAdminUserAsync(
        CancellationToken cancellationToken)
    {
        const string adminEmail = "admin@mova.com";
        const string adminPublicId = "MovaAdmin1";

        using var op = OperationLogger.Start(
            _logger,
            "SeedAdminUser",
            [
                ("Email", adminEmail),
                ("PublicId", adminPublicId),
            ]);

        await IdentitySeeder.SeedRolesAsync(_roleManager);

        var existing = await _userManager.FindByEmailAsync(adminEmail);
        if (existing is not null)
        {
            op.Success($"Bootstrap admin {adminEmail} already exists — skipping.");
            return;
        }

        var admin = new User
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,

            PhoneNumber = null,
            PhoneNumberConfirmed = true,

            PublicId = adminPublicId,
            FirstName = "Mova",
            LastName = "Admin",
            ProfilePicture = null,
            TransactionPinHash = null,
            BvnHash = null,

            Balance = Money.FromNaira(0),
            AccountStatus = UserAccountStatus.Active,
            RestrictionReason = AccountRestrictionReason.None,

            NotifyLoginAlerts = true,
            NotifyReleaseAlerts = true,
            NotifyProductUpdates = true,
            NotifyPromotions = false,

            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "DatabaseSeeder",
            IsDeleted = false,
            LockoutEnabled = true,
        };

        var createResult = await _userManager.CreateAsync(admin);

        if (!createResult.Succeeded)
        {
            var isDuplicate = createResult.Errors.Any(e =>
                e.Code is "DuplicateEmail" or "DuplicateUserName");

            if (isDuplicate)
            {
                op.Success(
                    $"Bootstrap admin {adminEmail} was created concurrently — skipping.");
                return;
            }

            op.Error(
                "Failed to seed bootstrap admin: " +
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return;
        }

        var rolesToAssign = new[] { Roles.SuperAdmin, Roles.Admin };

        foreach (var role in rolesToAssign)
        {
            var roleResult = await _userManager.AddToRoleAsync(admin, role);
            if (!roleResult.Succeeded)
            {
                op.Error(
                    $"Failed to assign {role}: " +
                    string.Join(", ", roleResult.Errors.Select(e => e.Description)));
                return;
            }
        }

        op.Success(
            $"Bootstrapped admin {adminEmail} with roles [{string.Join(", ", rolesToAssign)}]. " +
            "No password set — use the password-reset flow to activate.");
    }

    private async Task SeedBanksIfMissingAsync(
        CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "SeedBanksIfMissing");

        var bankCountBefore = await _dbContext.Banks
            .CountAsync(cancellationToken);

        if (bankCountBefore > 0)
        {
            op.Success($"Banks already present ({bankCountBefore}) — skipping refresh.");
            return;
        }

        try
        {
            await _bankService.RefreshBanksAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Local dev with no internet, or provider is down.
            _logger.LogWarning(ex, "Bank refresh could not reach the provider.");
            op.Warning(
                "Bank refresh could not reach the payment provider. " +
                "/health/ready will stay unhealthy until banks are populated.");
            return;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout — treat the same as a network failure.
            _logger.LogWarning(ex, "Bank refresh timed out.");
            op.Warning(
                "Bank refresh timed out. " +
                "/health/ready will stay unhealthy until banks are populated.");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bank refresh failed at startup.");
            op.Warning(
                "Bank refresh failed. /health/ready will stay unhealthy " +
                "until banks are populated.");
            return;
        }

        var bankCountAfter = await _dbContext.Banks
            .CountAsync(cancellationToken);

        if (bankCountAfter == 0)
        {
            op.Warning(
                "Bank refresh completed but no banks were inserted. " +
                "The provider may be returning an empty list.");
            return;
        }

        op.Success($"Bank refresh completed. Banks available: {bankCountAfter}.");
    }
}