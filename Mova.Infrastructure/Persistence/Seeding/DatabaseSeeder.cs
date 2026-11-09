using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
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
    private readonly ICacheService _cache;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        ApplicationDbContext dbContext,
        UserManager<User> userManager,
        RoleManager<IdentityRole<long>> roleManager,
        IBankService bankService,
        ICacheService cache,
        ILogger<DatabaseSeeder> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _bankService = bankService;
        _cache = cache;
        _logger = logger;
    }

    public async Task SeedAsync(
        CancellationToken cancellationToken = default)
    {
        await SeedWalletCategoriesAsync(cancellationToken);
        await SeedFeatureFlagsAsync(cancellationToken);
        await SeedLegalDocumentsAsync(cancellationToken);
        await SeedWalletTemplatesAsync(cancellationToken);
        await SeedAdminUserAsync(cancellationToken);
        await SeedBanksIfMissingAsync(cancellationToken);
    }

    private async Task SeedWalletCategoriesAsync(CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "SeedWalletCategories");

        var defaultCategories = DefaultWalletCategories.Create();

        var existingCategories = await _dbContext.WalletCategories
            .ToListAsync(cancellationToken);

        var existingById = existingCategories.ToDictionary(x => x.Id);

        var hasTypeDrift = defaultCategories.Any(d =>
            existingById.TryGetValue(d.Id, out var e) &&
            e.WalletType != d.WalletType);

        if (hasTypeDrift)
        {
            foreach (var defaultCategory in defaultCategories)
            {
                if (!existingById.TryGetValue(defaultCategory.Id, out var existing))
                    continue;

                existing.Name = defaultCategory.Name;
                existing.Icon = defaultCategory.Icon;
                existing.WalletType = defaultCategory.WalletType;
            }
        }

        var added = 0;

        foreach (var defaultCategory in defaultCategories)
        {
            if (existingById.ContainsKey(defaultCategory.Id))
                continue;

            await _dbContext.WalletCategories.AddAsync(
                defaultCategory,
                cancellationToken);

            added++;
        }

        if (added == 0 && !hasTypeDrift)
        {
            op.Success("Wallet categories already seeded — nothing to do.");
            return;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _cache.DeletePrefixAsync(CacheKeys.WalletTemplatesPrefix, cancellationToken);
        await _cache.DeletePrefixAsync(CacheKeys.WalletTrusteesPrefix, cancellationToken);

        var summary = hasTypeDrift
            ? $"Type drift detected — all records re-synced. {added} added."
            : $"{added} added.";

        op.Success($"Seeded wallet categories — {summary}");
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
        await _cache.DeletePrefixAsync(CacheKeys.FeatureFlagsPrefix, cancellationToken);

        op.Success($"Seeded {flagsToAdd.Count} feature flag(s).");
    }

    private async Task SeedLegalDocumentsAsync(CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "SeedLegalDocuments");

        var nextYear = DateTimeOffset.UtcNow.Year + 1;
        var version = $"{nextYear}-v1";

        var defaults = new[]
        {
            (Type: LegalDocumentType.Agreement, Title: "User Agreement"),
            (Type: LegalDocumentType.Privacy,   Title: "Privacy Policy"),
            (Type: LegalDocumentType.Terms,     Title: "Terms of Service"),
        };

        var existingCurrentTypes = (await _dbContext.LegalDocuments
            .Where(d => d.IsCurrent)
            .Select(d => d.Type)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var toAdd = new List<LegalDocument>();

        foreach (var (type, title) in defaults)
        {
            if (existingCurrentTypes.Contains(type))
                continue;

            toAdd.Add(new LegalDocument
            {
                Type = type,
                Version = version,
                Title = title,
                Content = string.Empty,
                PublishedAt = DateTimeOffset.UtcNow,
                IsCurrent = true,
            });
        }

        if (toAdd.Count == 0)
        {
            op.Success("Legal documents already seeded — nothing to do.");
            return;
        }

        await _dbContext.LegalDocuments.AddRangeAsync(toAdd, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _cache.DeletePrefixAsync(CacheKeys.LegalDocumentsPrefix, cancellationToken);

        op.Success(
            $"Seeded {toAdd.Count} legal document(s) with version {version}. " +
            "Content is empty — publish real text via the admin endpoint when ready.");
    }

    private async Task SeedWalletTemplatesAsync(CancellationToken cancellationToken)
    {
        using var op = OperationLogger.Start(_logger, "SeedWalletTemplates");

        var defaultTemplates = DefaultWalletTemplates.Create();

        var existingTemplates = await _dbContext.WalletTemplates
            .ToListAsync(cancellationToken);

        var existingByName = existingTemplates
            .GroupBy(x => x.Name)
            .ToDictionary(g => g.Key, g => g.First());

        var hasDrift = defaultTemplates.Any(d =>
            existingByName.TryGetValue(d.Name, out var e) &&
            (e.CategoryId != d.CategoryId || e.WalletType != d.WalletType));

        if (hasDrift)
        {
            foreach (var defaultTemplate in defaultTemplates)
            {
                if (!existingByName.TryGetValue(defaultTemplate.Name, out var existing))
                    continue;

                existing.Description = defaultTemplate.Description;
                existing.CategoryId = defaultTemplate.CategoryId;
                existing.WalletType = defaultTemplate.WalletType;
                existing.DefaultTargetAmount = defaultTemplate.DefaultTargetAmount;
                existing.DefaultReleaseAmount = defaultTemplate.DefaultReleaseAmount;
                existing.DefaultFrequency = defaultTemplate.DefaultFrequency;
                existing.DefaultFrequencyConfig = defaultTemplate.DefaultFrequencyConfig;
                existing.DefaultPayoutDestination = defaultTemplate.DefaultPayoutDestination;
                existing.IconName = defaultTemplate.IconName;
                existing.Tags = defaultTemplate.Tags;
                existing.SortOrder = defaultTemplate.SortOrder;
                existing.IsActive = defaultTemplate.IsActive;
            }
        }

        var added = 0;

        foreach (var defaultTemplate in defaultTemplates)
        {
            if (existingByName.ContainsKey(defaultTemplate.Name))
                continue;

            await _dbContext.WalletTemplates.AddAsync(
                defaultTemplate,
                cancellationToken);

            added++;
        }

        if (added == 0 && !hasDrift)
        {
            op.Success("Wallet templates already seeded — nothing to do.");
            return;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _cache.DeletePrefixAsync(CacheKeys.WalletTemplatesPrefix, cancellationToken);

        var summary = hasDrift
            ? $"Drift detected — all templates re-synced. {added} added."
            : $"{added} added.";

        op.Success($"Seeded wallet templates — {summary}");
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
            _logger.LogWarning(ex, "Bank refresh could not reach the provider.");
            op.Warning(
                "Bank refresh could not reach the payment provider. " +
                "/health/ready will stay unhealthy until banks are populated.");
            return;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
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
        
        await _cache.DeletePrefixAsync(CacheKeys.BanksPrefix, cancellationToken);
    }
}