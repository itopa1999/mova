using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;
using Mova.Shared.Logging;
using StackExchange.Redis;

namespace Mova.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private const string CachePrefix = "mova:cache";
    private const string CacheNull = "**NULL**";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _database = redis.GetDatabase();
        _logger = logger;
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

        var cacheKey = BuildKey(key);
        var lockKey = $"{cacheKey}:lock";
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var effectiveLockTimeout = lockTimeout ?? DefaultLockTimeout;
        var effectiveMaxWait = maxWait ?? DefaultMaxWait;

        using var op = OperationLogger.Start(_logger, "CacheGetOrSet", ("CacheKey", cacheKey));

        try
        {
            var cached = await TryGetAsync<T>(cacheKey);
            if (cached.Found)
            {
                op.Success("Cache hit.");
                return cached.Value;
            }

            var lockToken = Guid.NewGuid().ToString("N");
            var acquired = await _database.LockTakeAsync(lockKey, lockToken, effectiveLockTimeout);

            if (acquired)
            {
                try
                {
                    // Another request may have populated the value just before this lock was acquired.
                    cached = await TryGetAsync<T>(cacheKey);
                    if (cached.Found)
                    {
                        op.Success("Cache filled while acquiring lock.");
                        return cached.Value;
                    }

                    var value = await callback(cancellationToken);
                    await SetValueAsync(cacheKey, value, effectiveTimeout);
                    op.Success("Cache populated.");
                    return value;
                }
                finally
                {
                    await _database.LockReleaseAsync(lockKey, lockToken);
                }
            }

            var stopwatch = Stopwatch.StartNew();
            var waitInterval = TimeSpan.FromMilliseconds(100);

            while (stopwatch.Elapsed < effectiveMaxWait)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cached = await TryGetAsync<T>(cacheKey);
                if (cached.Found)
                {
                    op.Success("Cache populated by lock holder.");
                    return cached.Value;
                }

                await Task.Delay(waitInterval, cancellationToken);
                waitInterval = TimeSpan.FromMilliseconds(Math.Min(waitInterval.TotalMilliseconds * 1.5, 500));
            }

            cached = await TryGetAsync<T>(cacheKey);
            if (cached.Found)
            {
                op.Success("Cache populated before fallback.");
                return cached.Value;
            }

            var fallbackValue = await callback(cancellationToken);
            await SetValueAsync(cacheKey, fallbackValue, effectiveTimeout);
            op.Success("Cache populated by fallback.");
            return fallbackValue;
        }
        catch (Exception ex)
        {
            op.Fail($"Cache get-or-set failed for key '{cacheKey}'.", ex);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = BuildKey(key);
        using var op = OperationLogger.Start(_logger, "CacheDelete", ("CacheKey", cacheKey));

        try
        {
            var deleted = await _database.KeyDeleteAsync(cacheKey);
            op.Success(deleted ? "Cache key deleted." : "Cache key did not exist.");
            return deleted;
        }
        catch (Exception ex)
        {
            op.Fail($"Cache delete failed for key '{cacheKey}'.", ex);
            return false;
        }
    }

    public async Task<bool> DeletePrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        cancellationToken.ThrowIfCancellationRequested();

        var cachePrefix = BuildKey(prefix);
        using var op = OperationLogger.Start(_logger, "CacheDeletePrefix", ("CachePrefix", cachePrefix));

        try
        {
            var keys = new List<RedisKey>();
            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);
                if (server.IsReplica)
                {
                    continue;
                }

                keys.AddRange(server.Keys(_database.Database, $"{cachePrefix}*"));
            }

            if (keys.Count > 0)
            {
                await _database.KeyDeleteAsync(keys.Distinct().ToArray());
            }

            op.Success($"Deleted {keys.Count} cache key(s).");
            return true;
        }
        catch (Exception ex)
        {
            op.Fail($"Cache prefix delete failed for prefix '{cachePrefix}'.", ex);
            return false;
        }
    }

    private static string BuildKey(string key) => $"{CachePrefix}:{key}";

    private async Task<(bool Found, T? Value)> TryGetAsync<T>(RedisKey cacheKey)
    {
        var payload = await _database.StringGetAsync(cacheKey);
        if (payload.IsNull)
        {
            return (false, default);
        }

        if (payload == CacheNull)
        {
            return (true, default);
        }

        return (true, JsonSerializer.Deserialize<T>(payload!, SerializerOptions));
    }

    private Task SetValueAsync<T>(RedisKey cacheKey, T? value, TimeSpan timeout)
    {
        var payload = value is null
            ? CacheNull
            : JsonSerializer.Serialize(value, SerializerOptions);

        return _database.StringSetAsync(cacheKey, payload, timeout);
    }
}
