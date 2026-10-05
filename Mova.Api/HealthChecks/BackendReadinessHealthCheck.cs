using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Mova.Domain.Enums;
using Mova.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Mova.Api.HealthChecks;

public sealed class BackendReadinessHealthCheck : IHealthCheck
{
    private static readonly FeatureFlagName[] DepositProviderFlags =
    [
        FeatureFlagName.DepositViaPaystack,
        FeatureFlagName.DepositViaMonnify,
        FeatureFlagName.DepositViaFlutterwave
    ];

    private static readonly FeatureFlagName[] PayoutProviderFlags =
    [
        FeatureFlagName.PayoutsViaPaystack,
        FeatureFlagName.PayoutsViaMonnify,
        FeatureFlagName.PayoutsViaFlutterwave
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<BackendReadinessHealthCheck> _logger;

    public BackendReadinessHealthCheck(
        ApplicationDbContext dbContext,
        IConnectionMultiplexer redis,
        ILogger<BackendReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var checks = new Dictionary<string, object>();
        var isHealthy = true;

        await RunCheckAsync(
            "database",
            CheckDatabaseAsync,
            checks,
            healthy => isHealthy &= healthy,
            cancellationToken);

        await RunCheckAsync(
            "banks",
            CheckBanksAsync,
            checks,
            healthy => isHealthy &= healthy,
            cancellationToken);

        await RunCheckAsync(
            "redis",
            CheckRedisAsync,
            checks,
            healthy => isHealthy &= healthy,
            cancellationToken);

        await RunCheckAsync(
            "depositFeatureFlags",
            CheckDepositFeatureFlagsAsync,
            checks,
            healthy => isHealthy &= healthy,
            cancellationToken);

        await RunCheckAsync(
            "payoutFeatureFlags",
            CheckPayoutFeatureFlagsAsync,
            checks,
            healthy => isHealthy &= healthy,
            cancellationToken);

        await RunCheckAsync(
            "walletTemplates",
            CheckWalletTemplatesAsync,
            checks,
            healthy => isHealthy &= healthy,
            cancellationToken);

        return isHealthy
            ? HealthCheckResult.Healthy("All backend readiness checks passed.", checks)
            : HealthCheckResult.Unhealthy(
                "One or more backend readiness checks failed.",
                data: checks);
    }

    private async Task<CheckResult> CheckDatabaseAsync(
        CancellationToken cancellationToken)
    {
        if (!await _dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return CheckResult.Failed("Database connection failed.");
        }

        var pendingMigrations = await _dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken);
        var pendingCount = pendingMigrations.Count();

        return pendingCount == 0
            ? CheckResult.Passed("Database is reachable and migrations are applied.")
            : CheckResult.Failed(
                $"Database is reachable but has {pendingCount} pending migration(s).");
    }

    private async Task<CheckResult> CheckBanksAsync(
        CancellationToken cancellationToken)
    {
        var activeBankCount = await _dbContext.Banks
            .CountAsync(bank => bank.IsActive, cancellationToken);

        return activeBankCount > 0
            ? CheckResult.Passed($"{activeBankCount} active bank(s) are available.")
            : CheckResult.Failed("No active banks are populated.");
    }

    private async Task<CheckResult> CheckRedisAsync(
        CancellationToken cancellationToken)
    {
        if (!_redis.IsConnected)
        {
            return CheckResult.Failed("Redis is not connected.");
        }

        var database = _redis.GetDatabase();
        await database.PingAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var key = (RedisKey)$"mova:health:readiness:{Guid.NewGuid():N}";
        var expectedValue = Guid.NewGuid().ToString("N");

        var stored = await database.StringSetAsync(
            key,
            expectedValue,
            expiry: TimeSpan.FromSeconds(30),
            when: When.NotExists);

        if (!stored)
        {
            return CheckResult.Failed("Redis write check failed.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actualValue = await database.StringGetAsync(key);

            return actualValue == expectedValue
                ? CheckResult.Passed("Redis ping and write/read checks passed.")
                : CheckResult.Failed("Redis read check failed.");
        }
        finally
        {
            await database.KeyDeleteAsync(key);
        }
    }

    private async Task<CheckResult> CheckDepositFeatureFlagsAsync(
        CancellationToken cancellationToken)
    {
        var flags = await LoadFeatureFlagsAsync(cancellationToken);
        var depositEnabled = flags.TryGetValue(
            FeatureFlagName.AllowDepositFunds,
            out var depositsAllowed) && depositsAllowed;
        var providerEnabled = DepositProviderFlags.Any(
            flag => flags.TryGetValue(flag, out var enabled) && enabled);

        return depositEnabled && providerEnabled
            ? CheckResult.Passed("Deposits are enabled with at least one payment provider.")
            : CheckResult.Failed(
                "AllowDepositFunds and at least one DepositVia* provider flag must be enabled.");
    }

    private async Task<CheckResult> CheckPayoutFeatureFlagsAsync(
        CancellationToken cancellationToken)
    {
        var flags = await LoadFeatureFlagsAsync(cancellationToken);
        var payoutsEnabled = flags.TryGetValue(
            FeatureFlagName.AllowWithdrawFunds,
            out var withdrawalsAllowed) && withdrawalsAllowed;
        var providerEnabled = PayoutProviderFlags.Any(
            flag => flags.TryGetValue(flag, out var enabled) && enabled);

        return payoutsEnabled && providerEnabled
            ? CheckResult.Passed("Payouts are enabled with at least one payment provider.")
            : CheckResult.Failed(
                "AllowWithdrawFunds and at least one PayoutsVia* provider flag must be enabled.");
    }

    private Task<Dictionary<FeatureFlagName, bool>> LoadFeatureFlagsAsync(
        CancellationToken cancellationToken)
    {
        return _dbContext.FeatureFlags
            .AsNoTracking()
            .ToDictionaryAsync(
                flag => flag.Name,
                flag => flag.IsEnabled,
                cancellationToken);
    }

    private async Task<CheckResult> CheckWalletTemplatesAsync(
        CancellationToken cancellationToken)
    {
        var activeTemplateCount = await _dbContext.WalletTemplates
            .CountAsync(template => template.IsActive, cancellationToken);

        if (activeTemplateCount == 0)
        {
            return CheckResult.Failed("No active wallet templates are populated.");
        }

        var templateHasMissingCategory = await _dbContext.WalletTemplates
            .Where(template => template.IsActive)
            .AnyAsync(
                template => !_dbContext.WalletCategories
                    .Any(category => category.Id == template.CategoryId),
                cancellationToken);

        return templateHasMissingCategory
            ? CheckResult.Failed(
                "One or more active wallet templates reference a missing category.")
            : CheckResult.Passed(
                $"{activeTemplateCount} active wallet template(s) reference valid categories.");
    }

    private async Task RunCheckAsync(
        string name,
        Func<CancellationToken, Task<CheckResult>> check,
        IDictionary<string, object> results,
        Action<bool> setOverallHealth,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await check(cancellationToken);
            results[name] = new ReadinessCheckResult(
                result.IsHealthy ? "Healthy" : "Unhealthy",
                result.Message);
            setOverallHealth(result.IsHealthy);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Backend readiness check {CheckName} failed.", name);
            results[name] = new ReadinessCheckResult(
                "Unhealthy",
                "The check could not be completed.");
            setOverallHealth(false);
        }
    }

    private sealed record CheckResult(bool IsHealthy, string Message)
    {
        public static CheckResult Passed(string message) => new(true, message);
        public static CheckResult Failed(string message) => new(false, message);
    }

    public sealed record ReadinessCheckResult(string Status, string Message);
}
