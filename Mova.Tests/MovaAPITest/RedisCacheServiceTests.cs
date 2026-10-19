using System.Net;
using System.Text.Json;
using Moq;
using StackExchange.Redis;
using Mova.Infrastructure.Caching;
using Microsoft.Extensions.Logging;


namespace Mova.Tests.Caching;

public sealed class RedisCacheServiceUnitTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _database = new();
    private readonly Mock<ILogger<RedisCacheService>> _logger = new();

    private RedisCacheService CreateService()
    {
        _redis
            .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_database.Object);

        _database
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _database
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>()))
            .ReturnsAsync(true);

        return new RedisCacheService(_redis.Object, _logger.Object);
    }

    private static string JsonWeb<T>(T value) =>
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private List<Moq.IInvocation> GetStringSetInvocations() =>
        _database.Invocations
            .Where(i => i.Method.Name == nameof(IDatabase.StringSetAsync))
            .ToList();

    // ═══════════════════════════════════════════════════════════
    // GetOrSetFastAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task GetOrSetFastAsync_OnCacheHit_ReturnsCachedValueAndSkipsCallback()
    {
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)JsonWeb("cached-value"));

        var callbackInvoked = false;
        var service = CreateService();

        var result = await service.GetOrSetFastAsync<string>(
            "test-key",
            _ => { callbackInvoked = true; return Task.FromResult<string?>("fresh"); });

        Assert.Equal("cached-value", result);
        Assert.False(callbackInvoked);
    }

    [Fact]
    public async Task GetOrSetFastAsync_OnCacheMiss_InvokesCallbackAndCachesResult()
    {
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var service = CreateService();
        var result = await service.GetOrSetFastAsync<string>(
            "test-key",
            _ => Task.FromResult<string?>("fresh"));

        Assert.Equal("fresh", result);

        // Inspect invocations directly — bypasses Moq's It.IsAny<TimeSpan?>() matcher.
        var stringSetCalls = GetStringSetInvocations();
        var call = Assert.Single(stringSetCalls);
        Assert.Equal("mova:cache:test-key", call.Arguments[0].ToString());
        Assert.Contains("fresh", call.Arguments[1].ToString());
    }

    [Fact]
    public async Task GetOrSetFastAsync_WhenCallbackReturnsNull_DoesNotCache()
    {
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var service = CreateService();
        var result = await service.GetOrSetFastAsync<string>(
            "test-key",
            _ => Task.FromResult<string?>(null));

        Assert.Null(result);
        Assert.Empty(GetStringSetInvocations());
    }

    [Fact]
    public async Task GetOrSetFastAsync_WhenRedisThrows_FallsThroughToCallback()
    {
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        _database
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var service = CreateService();
        var result = await service.GetOrSetFastAsync<string>(
            "test-key",
            _ => Task.FromResult<string?>("fresh"));

        Assert.Equal("fresh", result);
    }

    [Fact]
    public async Task GetOrSetFastAsync_WhenJsonIsCorrupt_FallsThroughToCallback()
    {
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"not-valid-json");

        var service = CreateService();
        var result = await service.GetOrSetFastAsync<string>(
            "test-key",
            _ => Task.FromResult<string?>("fresh"));

        Assert.Equal("fresh", result);
    }

    // ═══════════════════════════════════════════════════════════
    // GetOrSetAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task GetOrSetAsync_OnCacheHit_ReturnsCachedValueAndSkipsLockAndCallback()
    {
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)JsonWeb("cached-value"));

        var callbackInvoked = false;
        var service = CreateService();

        var result = await service.GetOrSetAsync<string>(
            "test-key",
            _ => { callbackInvoked = true; return Task.FromResult<string?>("fresh"); });

        Assert.Equal("cached-value", result);
        Assert.False(callbackInvoked);

        var lockAcquisitions = GetStringSetInvocations()
            .Where(i => i.Arguments[0].ToString().EndsWith(":lock"))
            .ToList();
        Assert.Empty(lockAcquisitions);
    }

    [Fact]
    public async Task GetOrSetAsync_OnCacheMiss_AcquiresLockAndCachesResult()
    {
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisResult)null!);

        var service = CreateService();
        var result = await service.GetOrSetAsync<string>(
            "test-key",
            _ => Task.FromResult<string?>("fresh"));

        Assert.Equal("fresh", result);

        var invocations = GetStringSetInvocations();
        Assert.Contains(invocations, i => i.Arguments[0].ToString() == "mova:cache:test-key:lock");

        _database.Verify(x => x.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenCallbackThrows_ReleasesLock()
    {
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _database
            .Setup(x => x.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisResult)null!);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetOrSetAsync<string>(
                "test-key",
                _ => throw new InvalidOperationException("boom")));

        _database.Verify(x => x.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0].ToString() == "mova:cache:test-key:lock"),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenLockNotAcquired_PollsUntilPopulated()
    {
        // Create the service FIRST so the overrides below are added LAST (Moq last-wins).
        var service = CreateService();

        var getCalls = 0;
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() =>
            {
                getCalls++;
                return getCalls == 1
                    ? RedisValue.Null
                    : (RedisValue)JsonWeb("cached-value");
            });

        // This override must come AFTER CreateService(), otherwise CreateService's
        // any-When → true setup wins and the lock is acquired (callback runs).
        _database
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                When.NotExists))
            .ReturnsAsync(false);

        var callbackInvoked = false;

        var result = await service.GetOrSetAsync<string>(
            "test-key",
            _ => { callbackInvoked = true; return Task.FromResult<string?>("fresh"); },
            maxWait: TimeSpan.FromMilliseconds(500));

        Assert.Equal("cached-value", result);
        Assert.False(callbackInvoked);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenLockNotAcquiredAndTimeoutExpires_FallsThroughToCallback()
    {
        var service = CreateService();

        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _database
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                When.NotExists))
            .ReturnsAsync(false);

        var callbackInvoked = false;

        var result = await service.GetOrSetAsync<string>(
            "test-key",
            _ => { callbackInvoked = true; return Task.FromResult<string?>("fresh"); },
            maxWait: TimeSpan.FromMilliseconds(100));

        Assert.Equal("fresh", result);
        Assert.True(callbackInvoked);
    }

    [Fact]
    public async Task GetOrSetAsync_WhenLockAcquisitionThrows_FallsThroughToCallback()
    {
        var service = CreateService();

        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _database
            .Setup(x => x.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                When.NotExists))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var callbackInvoked = false;

        var result = await service.GetOrSetAsync<string>(
            "test-key",
            _ => { callbackInvoked = true; return Task.FromResult<string?>("fresh"); });

        Assert.Equal("fresh", result);
        Assert.True(callbackInvoked);
    }

    // ═══════════════════════════════════════════════════════════
    // DeleteAsync
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task DeleteAsync_OnSuccess_ReturnsTrue()
    {
        _database
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = CreateService();
        var result = await service.DeleteAsync("some-key");

        Assert.True(result);
    }

    [Fact]
    public async Task DeleteAsync_OnFailure_ReturnsFalse()
    {
        _database
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var service = CreateService();
        var result = await service.DeleteAsync("some-key");

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_UsesPrefixedKey()
    {
        _database
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = CreateService();
        await service.DeleteAsync("some-key");

        _database.Verify(x => x.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == "mova:cache:some-key"),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    
[Fact]
public async Task DeleteAsync_ReadBackThroughService_ShowsKeyIsGone()
{
    // Arrange: create fresh, isolated Redis mocks and an in-memory store.
    var freshDatabase = new Mock<IDatabase>();
    var freshRedis = new Mock<IConnectionMultiplexer>();
    var store = new Dictionary<string, string>();

    freshRedis
        .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
        .Returns(freshDatabase.Object);

    // FIX: Configure the StringSetAsync call used by RedisCacheService.
    freshDatabase
    .Setup(x => x.StringSetAsync(
        It.IsAny<RedisKey>(),
        It.IsAny<RedisValue>(),
        It.IsAny<Expiration>(),
        It.IsAny<ValueCondition>(),
        It.IsAny<CommandFlags>()))
    .ReturnsAsync((
        RedisKey key,
        RedisValue value,
        Expiration expiry,
        ValueCondition condition,
        CommandFlags flags) =>
    {
        store[key.ToString()] = value.ToString();
        return true;
    });

    // Read values from the in-memory store.
    freshDatabase
        .Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
        .ReturnsAsync((RedisKey key, CommandFlags flags) =>
            store.TryGetValue(key.ToString(), out var value)
                ? (RedisValue)value
                : RedisValue.Null);

    // Delete values from the in-memory store.
    freshDatabase
        .Setup(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
        .ReturnsAsync((RedisKey key, CommandFlags flags) =>
            store.Remove(key.ToString()));

    var service = new RedisCacheService(
    freshRedis.Object,
    Mock.Of<ILogger<RedisCacheService>>());

    // 1. Cache miss: callback runs and the value is stored.
    var write = await service.GetOrSetFastAsync<string>(
        "readback-key",
        _ => Task.FromResult<string?>("original"));

    Assert.Equal("original", write);
    Assert.True(
        store.ContainsKey("mova:cache:readback-key"),
        $"Expected cached key. Keys: [{string.Join(", ", store.Keys)}]");

    // 2. Cache hit: return the cached value without invoking the callback.
    var hit = await service.GetOrSetFastAsync<string>(
        "readback-key",
        _ => throw new InvalidOperationException(
            "Callback must not run on a cache hit."));

    Assert.Equal("original", hit);

    // 3. Delete the cached value.
    var deleted = await service.DeleteAsync("readback-key");

    Assert.True(deleted);
    Assert.False(
        store.ContainsKey("mova:cache:readback-key"),
        $"Expected key to be deleted. Keys: [{string.Join(", ", store.Keys)}]");

    // 4. Cache miss again: callback runs and returns the fallback value.
    var after = await service.GetOrSetFastAsync<string>(
        "readback-key",
        _ => Task.FromResult<string?>("fallback"));

    Assert.Equal("fallback", after);
}


    // ═══════════════════════════════════════════════════════════
    // DeletePrefixAsync
    // ═══════════════════════════════════════════════════════════

    private void SetupSingleEndpoint(bool isReplica, IEnumerable<RedisKey> keys)
    {
        var endpoint = new DnsEndPoint("localhost", 6379);
        var server = new Mock<IServer>();
        server.Setup(x => x.IsConnected).Returns(true);
        server.Setup(x => x.IsReplica).Returns(isReplica);
        server
            .Setup(x => x.Keys(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(keys);

        _redis.Setup(x => x.GetEndPoints(It.IsAny<bool>())).Returns(new EndPoint[] { endpoint });
        _redis.Setup(x => x.GetServer(endpoint, It.IsAny<object>())).Returns(server.Object);
    }

    [Fact]
    public async Task DeletePrefixAsync_WhenKeysExist_DeletesAndReturnsTrue()
    {
        SetupSingleEndpoint(
            isReplica: false,
            keys: new RedisKey[]
            {
                "mova:cache:banks:1",
                "mova:cache:banks:2",
            });
        _database
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(2);

        var service = CreateService();
        var result = await service.DeletePrefixAsync("banks:");

        Assert.True(result);
        _database.Verify(x => x.KeyDeleteAsync(
            It.Is<RedisKey[]>(arr => arr.Length == 2),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task DeletePrefixAsync_WhenNoKeys_ReturnsFalse()
    {
        SetupSingleEndpoint(isReplica: false, keys: Array.Empty<RedisKey>());

        var service = CreateService();
        var result = await service.DeletePrefixAsync("banks:");

        Assert.False(result);
    }

    [Fact]
    public async Task DeletePrefixAsync_OnFailure_ReturnsFalse()
    {
        _redis
            .Setup(x => x.GetEndPoints(It.IsAny<bool>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var service = CreateService();
        var result = await service.DeletePrefixAsync("banks:");

        Assert.False(result);
    }

    [Fact]
    public async Task DeletePrefixAsync_UsesPatternWithCachePrefix()
    {
        var endpoint = new DnsEndPoint("localhost", 6379);
        var server = new Mock<IServer>();
        server.Setup(x => x.IsConnected).Returns(true);
        server.Setup(x => x.IsReplica).Returns(false);
        server
            .Setup(x => x.Keys(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(Array.Empty<RedisKey>());

        _redis.Setup(x => x.GetEndPoints(It.IsAny<bool>())).Returns(new EndPoint[] { endpoint });
        _redis.Setup(x => x.GetServer(endpoint, It.IsAny<object>())).Returns(server.Object);

        var service = CreateService();
        await service.DeletePrefixAsync("banks:");

        server.Verify(x => x.Keys(
            It.IsAny<int>(),
            It.Is<RedisValue>(p => p.ToString() == "mova:cache:banks:*"),
            It.IsAny<int>(),
            It.IsAny<long>(),
            It.IsAny<int>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task DeletePrefixAsync_SkipsReplicaServers()
    {
        SetupSingleEndpoint(
            isReplica: true,
            keys: new RedisKey[] { "mova:cache:banks:1" });

        var service = CreateService();
        var result = await service.DeletePrefixAsync("banks:");

        Assert.False(result);
        _database.Verify(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey[]>(),
            It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task DeletePrefixAsync_BatchesDeletesInChunks()
    {
        var keys = Enumerable.Range(0, 300)
            .Select(i => (RedisKey)$"mova:cache:banks:{i}")
            .ToArray();

        SetupSingleEndpoint(isReplica: false, keys: keys);
        _database
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(256L);

        var service = CreateService();
        var result = await service.DeletePrefixAsync("banks:");

        Assert.True(result);
        _database.Verify(x => x.KeyDeleteAsync(
            It.Is<RedisKey[]>(arr => arr.Length == 256),
            It.IsAny<CommandFlags>()),
            Times.Once);
        _database.Verify(x => x.KeyDeleteAsync(
            It.Is<RedisKey[]>(arr => arr.Length == 44),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════
    // Prefix is applied everywhere
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public async Task AllOperations_UseCachePrefix()
    {
        _database
            .Setup(x => x.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _database
            .Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = CreateService();
        await service.GetOrSetFastAsync<string>("k", _ => Task.FromResult<string?>("v"));
        await service.DeleteAsync("k");

        _database.Verify(x => x.StringGetAsync(
            It.Is<RedisKey>(k => k.ToString() == "mova:cache:k"),
            It.IsAny<CommandFlags>()),
            Times.Once);

        var stringSetCalls = GetStringSetInvocations();
        var call = Assert.Single(stringSetCalls);
        Assert.Equal("mova:cache:k", call.Arguments[0].ToString());

        _database.Verify(x => x.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString() == "mova:cache:k"),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    
[Fact]
public async Task GetOrSetFastAsync_WhenJsonIsCorrupt_RepopulatesCache()
{
    var freshDatabase = new Mock<IDatabase>();
    var freshRedis = new Mock<IConnectionMultiplexer>();
    var store = new Dictionary<string, string>
    {
        ["mova:cache:corrupt-key"] = "not-valid-json"
    };

    freshRedis
        .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
        .Returns(freshDatabase.Object);

    freshDatabase
        .Setup(x => x.StringGetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()))
        .ReturnsAsync((RedisKey key, CommandFlags flags) =>
            store.TryGetValue(key.ToString(), out var value)
                ? (RedisValue)value
                : RedisValue.Null);

    freshDatabase
    .Setup(x => x.StringSetAsync(
        It.IsAny<RedisKey>(),
        It.IsAny<RedisValue>(),
        It.IsAny<Expiration>(),
        It.IsAny<ValueCondition>(),
        It.IsAny<CommandFlags>()))
    .ReturnsAsync((
        RedisKey key,
        RedisValue value,
        Expiration expiry,
        ValueCondition condition,
        CommandFlags flags) =>
    {
        store[key.ToString()] = value.ToString();
        return true;
    });

    var service = new RedisCacheService(
    freshRedis.Object,
    Mock.Of<ILogger<RedisCacheService>>());
    var callbackCalls = 0;

    var result = await service.GetOrSetFastAsync<string>(
        "corrupt-key",
        _ =>
        {
            callbackCalls++;
            return Task.FromResult<string?>("recovered");
        });

    Assert.Equal("recovered", result);
    Assert.Equal(1, callbackCalls);
    Assert.Equal(
        "recovered",
        JsonSerializer.Deserialize<string>(
            store["mova:cache:corrupt-key"],
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}


[Fact]
public async Task DeletePrefixAsync_WhenLaterBatchFails_ReturnsFalse()
{
    var endpoint = new DnsEndPoint("localhost", 6379);
    var server = new Mock<IServer>();
    var database = new Mock<IDatabase>();

    var redis = new Mock<IConnectionMultiplexer>();

    var keys = Enumerable.Range(0, 300)
        .Select(i => (RedisKey)$"mova:cache:banks:{i}")
        .ToArray();

    server.SetupGet(x => x.IsConnected).Returns(true);
    server.SetupGet(x => x.IsReplica).Returns(false);

    server
        .Setup(x => x.Keys(
            It.IsAny<int>(),
            It.IsAny<RedisValue>(),
            It.IsAny<int>(),
            It.IsAny<long>(),
            It.IsAny<int>(),
            It.IsAny<CommandFlags>()))
        .Returns(keys);

    redis
        .Setup(x => x.GetEndPoints(It.IsAny<bool>()))
        .Returns(new EndPoint[] { endpoint });

    redis
        .Setup(x => x.GetServer(endpoint, It.IsAny<object>()))
        .Returns(server.Object);

    redis
        .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
        .Returns(database.Object);

    var batchCalls = 0;

    database
        .Setup(x => x.KeyDeleteAsync(
            It.IsAny<RedisKey[]>(),
            It.IsAny<CommandFlags>()))
        .Returns((RedisKey[] batch, CommandFlags flags) =>
        {
            batchCalls++;

            if (batchCalls == 1)
                return Task.FromResult((long)batch.Length);

            return Task.FromException<long>(
                new RedisConnectionException(
                    ConnectionFailureType.UnableToConnect,
                    "Simulated failure on second batch"));
        });

    var service = new RedisCacheService(
    redis.Object,
    Mock.Of<ILogger<RedisCacheService>>());

    var result = await service.DeletePrefixAsync("banks:");

    Assert.False(result);
    Assert.Equal(2, batchCalls);
}



[Fact]
public async Task DeleteAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
{
    var database = new Mock<IDatabase>();
    var redis = new Mock<IConnectionMultiplexer>();

    redis
        .Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
        .Returns(database.Object);

    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();

    var service = new RedisCacheService(
    redis.Object,
    Mock.Of<ILogger<RedisCacheService>>());

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => service.DeleteAsync("cancelled-key", cts.Token));

    database.Verify(
        x => x.KeyDeleteAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<CommandFlags>()),
        Times.Never);
}


}