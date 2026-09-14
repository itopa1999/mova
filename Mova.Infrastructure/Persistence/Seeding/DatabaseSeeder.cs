using Microsoft.EntityFrameworkCore;

namespace Mova.Infrastructure.Persistence.Seeding;

public sealed class DatabaseSeeder
{
    private readonly ApplicationDbContext _dbContext;

    public DatabaseSeeder(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(
        CancellationToken cancellationToken = default)
    {
        await SeedWalletCategoriesAsync(cancellationToken);
        await SeedFeatureFlagsAsync(cancellationToken);
    }

    private async Task SeedWalletCategoriesAsync(
        CancellationToken cancellationToken)
    {
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
            return;
        }

        await _dbContext.WalletCategories.AddRangeAsync(
            categoriesToAdd,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedFeatureFlagsAsync(
        CancellationToken cancellationToken)
    {
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
            return;
        }

        await _dbContext.FeatureFlags.AddRangeAsync(
            flagsToAdd,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}