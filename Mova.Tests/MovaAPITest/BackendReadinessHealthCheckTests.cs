using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Mova.Api.HealthChecks;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;
using Microsoft.EntityFrameworkCore;

namespace Mova.Tests.HealthChecks;

public sealed class BackendReadinessHealthCheckTests : BaseTest
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _redisDb = new();

    private BackendReadinessHealthCheck CreateCheck(bool redisConnected = true)
    {
        _redis.Setup(x => x.IsConnected).Returns(redisConnected);
        _redis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
              .Returns(_redisDb.Object);

        _redisDb.Setup(x => x.PingAsync(It.IsAny<CommandFlags>()))
                .ReturnsAsync(TimeSpan.FromMilliseconds(1));

        // Capture the value written so StringGetAsync can return it.
        string? stored = null;

        // Match the 4-arg overload the health check actually calls:
        //   StringSetAsync(key, value, expiry, when)
        _redisDb
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, When>(
                (_, value, _, _) => stored = value)
            .ReturnsAsync(true);

        // Also match the 5-arg overload (expiry, when, flags) for safety.
        _redisDb
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, When, CommandFlags>(
                (_, value, _, _, _) => stored = value)
            .ReturnsAsync(true);

        _redisDb
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() => stored);

        _redisDb
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        return new BackendReadinessHealthCheck(
            Context,
            _redis.Object,
            Mock.Of<ILogger<BackendReadinessHealthCheck>>());
    }

    private async Task SeedHealthyDataAsync()
    {
        // Banks
        await UnitOfWork.AddAsync(new Bank
        {
            Name = "GTBank",
            Code = "058",
            IsActive = true,
        });

        // Feature flags
        await UnitOfWork.AddAsync(new FeatureFlag
        {
            Name = FeatureFlagName.AllowDepositFunds,
            IsEnabled = true,
        });
        await UnitOfWork.AddAsync(new FeatureFlag
        {
            Name = FeatureFlagName.DepositViaPaystack,
            IsEnabled = true,
        });
        await UnitOfWork.AddAsync(new FeatureFlag
        {
            Name = FeatureFlagName.AllowWithdrawFunds,
            IsEnabled = true,
        });
        await UnitOfWork.AddAsync(new FeatureFlag
        {
            Name = FeatureFlagName.PayoutsViaPaystack,
            IsEnabled = true,
        });

        // Wallet category + template
        var category = new WalletCategory
        {
            Name = "Rent & Housing",
            Icon = "Wallet",
        };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();

        await UnitOfWork.AddAsync(new WalletTemplate
        {
            Name = "Rent Savings",
            IsActive = true,
            CategoryId = category.Id,
        });

        await UnitOfWork.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────
    // 1. Happy path
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_WhenAllChecksPass_ReturnsHealthy()
    {
        await SeedHealthyDataAsync();
        var check = CreateCheck();

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("database", result.Data.Keys);
        Assert.Contains("banks", result.Data.Keys);
        Assert.Contains("redis", result.Data.Keys);
        Assert.Contains("depositFeatureFlags", result.Data.Keys);
        Assert.Contains("payoutFeatureFlags", result.Data.Keys);
        Assert.Contains("walletTemplates", result.Data.Keys);
    }

    // ─────────────────────────────────────────────────────────
    // 2. Redis failures
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_WhenRedisDisconnected_ReturnsUnhealthy()
    {
        await SeedHealthyDataAsync();
        var check = CreateCheck(redisConnected: false);

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRedisPingThrows_ReturnsUnhealthy()
    {
        await SeedHealthyDataAsync();

        // CreateCheck() FIRST so its default mocks are registered,
        // then override PingAsync to throw. Moq's last setup wins.
        var check = CreateCheck();

        _redisDb.Setup(x => x.PingAsync(It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect, "boom"));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // ─────────────────────────────────────────────────────────
    // 3. Data failures
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_WhenNoActiveBanks_ReturnsUnhealthy()
    {
        // Skip bank seeding — only seed the rest.
        await UnitOfWork.AddAsync(new FeatureFlag { Name = FeatureFlagName.AllowDepositFunds, IsEnabled = true });
        await UnitOfWork.AddAsync(new FeatureFlag { Name = FeatureFlagName.DepositViaPaystack, IsEnabled = true });
        await UnitOfWork.AddAsync(new FeatureFlag { Name = FeatureFlagName.AllowWithdrawFunds, IsEnabled = true });
        await UnitOfWork.AddAsync(new FeatureFlag { Name = FeatureFlagName.PayoutsViaPaystack, IsEnabled = true });

        var category = new WalletCategory { Name = "Rent", Icon = "Wallet" };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();

        await UnitOfWork.AddAsync(new WalletTemplate
        {
            Name = "Rent Savings",
            IsActive = true,
            CategoryId = category.Id,
        });
        await UnitOfWork.SaveChangesAsync();

        var check = CreateCheck();
        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenDepositFeatureFlagsDisabled_ReturnsUnhealthy()
    {
        await SeedHealthyDataAsync();

        var flag = Context.FeatureFlags
            .First(f => f.Name == FeatureFlagName.AllowDepositFunds);
        flag.IsEnabled = false;
        await Context.SaveChangesAsync();

        var check = CreateCheck();
        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenNoActiveWalletTemplates_ReturnsUnhealthy()
    {
        await SeedHealthyDataAsync();

        foreach (var template in Context.WalletTemplates)
            template.IsActive = false;
        await Context.SaveChangesAsync();

        var check = CreateCheck();
        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // ─────────────────────────────────────────────────────────
    // 4. Robustness — one failing check must not crash the whole check
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_WhenOneCheckThrows_StillReturnsAResult()
    {
        await SeedHealthyDataAsync();

        // CreateCheck() FIRST so its default mocks are registered,
        // then override PingAsync to throw. Moq's last setup wins.
        var check = CreateCheck();

        _redisDb.Setup(x => x.PingAsync(It.IsAny<CommandFlags>()))
                .ThrowsAsync(new InvalidOperationException("kaboom"));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.True(result.Data.ContainsKey("redis"));
    }
}