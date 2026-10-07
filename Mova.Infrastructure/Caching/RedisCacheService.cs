using System.Text.Json;
using Mova.Application.Interfaces.Caching;
using StackExchange.Redis;

namespace Mova.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private const string CachePrefix = "mova:cache";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string ReleaseLockScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;

    public RedisCacheService(IConnectionMultiplexer redis)
    {
        _redis = redis;
        _database = redis.GetDatabase();
    }

    public async Task<T?> GetOrSetFastAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> callback,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = BuildKey(key);
        var cachedValue = await _database.StringGetAsync(cacheKey);
        if (cachedValue.HasValue)
        {
            return JsonSerializer.Deserialize<T>((byte[]?)cachedValue!, SerializerOptions);
        }

        var value = await callback(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await StoreAsync(cacheKey, value, timeout);
        return value;
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> callback,
        TimeSpan? timeout = null,
        TimeSpan? lockTimeout = null,
        TimeSpan? maxWait = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = BuildKey(key);
        var cachedValue = await _database.StringGetAsync(cacheKey);
        if (cachedValue.HasValue)
        {
            return JsonSerializer.Deserialize<T>((byte[]?)cachedValue!, SerializerOptions);
        }

        var lockKey = (RedisKey)$"{cacheKey}:lock";
        var lockToken = Guid.NewGuid().ToString("N");
        var effectiveLockTimeout = lockTimeout ?? DefaultLockTimeout;
        if (effectiveLockTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lockTimeout),
                "The cache lock timeout must be greater than zero.");
        }

        var lockAcquired = await _database.StringSetAsync(
            lockKey,
            lockToken,
            effectiveLockTimeout,
            When.NotExists);

        if (!lockAcquired)
        {
            var effectiveMaxWait = maxWait ?? DefaultMaxWait;
            if (effectiveMaxWait < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxWait),
                    "The maximum cache wait cannot be negative.");
            }

            var waitUntil = DateTimeOffset.UtcNow + effectiveMaxWait;
            while (DateTimeOffset.UtcNow < waitUntil)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

                cachedValue = await _database.StringGetAsync(cacheKey);
                if (cachedValue.HasValue)
                {
                    return JsonSerializer.Deserialize<T>(
                        (byte[]?)cachedValue!,
                        SerializerOptions);
                }
            }

            return await callback(cancellationToken);
        }

        try
        {
            cachedValue = await _database.StringGetAsync(cacheKey);
            if (cachedValue.HasValue)
            {
                return JsonSerializer.Deserialize<T>(
                    (byte[]?)cachedValue!,
                    SerializerOptions);
            }

            var value = await callback(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await StoreAsync(cacheKey, value, timeout);
            return value;
        }
        finally
        {
            await _database.ScriptEvaluateAsync(
                ReleaseLockScript,
                [lockKey],
                [lockToken]);
        }
    }

    public async Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        return await _database.KeyDeleteAsync(BuildKey(key));
    }

    public async Task<bool> DeletePrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        var pattern = $"{BuildKey(prefix)}*";
        var deletedAny = false;
        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica)
            {
                continue;
            }

            var keys = new List<RedisKey>(500);
            await foreach (var key in server.KeysAsync(
                database: _database.Database,
                pattern: pattern,
                pageSize: 500).WithCancellation(cancellationToken))
            {
                keys.Add(key);
                if (keys.Count < 500)
                {
                    continue;
                }

                deletedAny |= await _database.KeyDeleteAsync(keys.ToArray()) > 0;
                keys.Clear();
            }

            if (keys.Count > 0)
            {
                deletedAny |= await _database.KeyDeleteAsync(keys.ToArray()) > 0;
            }
        }

        return deletedAny;
    }

    private static string BuildKey(string key) => $"{CachePrefix}:{key}";

    private async Task StoreAsync<T>(
        RedisKey key,
        T? value,
        TimeSpan? timeout)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            return;
        }

        var serializedValue = JsonSerializer.SerializeToUtf8Bytes(
            value,
            SerializerOptions);
        await _database.StringSetAsync(key, serializedValue, effectiveTimeout);
    }
}