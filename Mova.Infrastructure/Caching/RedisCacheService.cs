
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;
using Mova.Shared.Logging;
using StackExchange.Redis;

namespace Mova.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private const string CachePrefix = "mova:cache";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private const string ReleaseLockScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

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

    public async Task<T?> GetOrSetFastAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> callback,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullKey = BuildKey(key);
        T? cached;

        try
        {
            var raw = await _database.StringGetAsync(fullKey);

            if (raw.HasValue)
            {
                try
                {
                    cached = JsonSerializer.Deserialize<T>(
                        raw.ToString(),
                        SerializerOptions);

                    return cached;
                }
                catch (JsonException ex)
                {
                    LogCacheError(
                        "GetOrSetFast.Deserialize",
                        key,
                        ex,
                        $"Failed to deserialize cached value for key '{key}'.");
                }
            }
        }
        catch (RedisException ex)
        {
            LogRedisError(
                "GetOrSetFast.Read",
                key,
                ex,
                $"Failed to read Redis cache key '{key}'. Falling back to the callback.");
            
            return await callback(cancellationToken);
        }

        var value = await callback(cancellationToken);

        if (value is null)
        {
            return value;
        }

        try
        {
            var serialized = JsonSerializer.Serialize(value, SerializerOptions);

            await _database.StringSetAsync(
                fullKey,
                serialized,
                timeout ?? DefaultTimeout);
        }
        catch (RedisException ex)
        {
            LogRedisError(
                "GetOrSetFast.Write",
                key,
                ex,
                $"Failed to write Redis cache key '{key}'. Returning the callback result.");
        }
        catch (JsonException ex)
        {
            LogCacheError(
                "GetOrSetFast.Serialize",
                key,
                ex,
                $"Failed to serialize value for Redis cache key '{key}'.");
        }

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
        cancellationToken.ThrowIfCancellationRequested();

        var fullKey = BuildKey(key);

        try
        {
            var raw = await _database.StringGetAsync(fullKey);

            if (raw.HasValue)
            {
                try
                {
                    return JsonSerializer.Deserialize<T>(
                        raw.ToString(),
                        SerializerOptions);
                }
                catch (JsonException ex)
                {
                    LogCacheError(
                        "GetOrSet.Deserialize",
                        key,
                        ex,
                        $"Failed to deserialize cached value for key '{key}'.");
                }
            }
        }
        catch (RedisException ex)
        {
            LogRedisError(
                "GetOrSet.Read",
                key,
                ex,
                $"Failed to read Redis cache key '{key}'. Falling back to the callback.");

            return await callback(cancellationToken);
        }

        var lockKey = $"{fullKey}:lock";
        var lockValue = Guid.NewGuid().ToString("N");
        var lockTtl = lockTimeout ?? DefaultLockTimeout;
        var waitFor = maxWait ?? DefaultMaxWait;

        bool acquired;

        try
        {
            acquired = await _database.StringSetAsync(
                lockKey,
                lockValue,
                lockTtl,
                When.NotExists);
        }
        catch (RedisException ex)
        {
            LogRedisError(
                "GetOrSet.AcquireLock",
                key,
                ex,
                $"Failed to acquire Redis cache lock for key '{key}'. Falling back to the callback.");

            return await callback(cancellationToken);
        }

        if (!acquired)
        {
            var deadline = DateTime.UtcNow + waitFor;

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50, cancellationToken);

                try
                {
                    var raw = await _database.StringGetAsync(fullKey);

                    if (raw.HasValue)
                    {
                        try
                        {
                            return JsonSerializer.Deserialize<T>(
                                raw.ToString(),
                                SerializerOptions);
                        }
                        catch (JsonException ex)
                        {
                            LogCacheError(
                                "GetOrSet.WaitDeserialize",
                                key,
                                ex,
                                $"Failed to deserialize cached value while waiting for key '{key}'.");
                        }
                    }
                }
                catch (RedisException ex)
                {
                    LogRedisError(
                        "GetOrSet.WaitForCache",
                        key,
                        ex,
                        $"Redis failed while waiting for cache key '{key}'. Falling back to the callback.");

                    return await callback(cancellationToken);
                }
            }

            return await callback(cancellationToken);
        }

        try
        {
            var value = await callback(cancellationToken);

            if (value is not null)
            {
                try
                {
                    var serialized = JsonSerializer.Serialize(
                        value,
                        SerializerOptions);

                    await _database.StringSetAsync(
                        fullKey,
                        serialized,
                        timeout ?? DefaultTimeout);
                }
                catch (RedisException ex)
                {
                    LogRedisError(
                        "GetOrSet.Write",
                        key,
                        ex,
                        $"Failed to write Redis cache key '{key}'. Returning the callback result.");
                }
                catch (JsonException ex)
                {
                    LogCacheError(
                        "GetOrSet.Serialize",
                        key,
                        ex,
                        $"Failed to serialize value for Redis cache key '{key}'.");
                }
            }

            return value;
        }
        finally
        {
            try
            {
                await _database.ScriptEvaluateAsync(
                    ReleaseLockScript,
                    [lockKey],
                    [lockValue]);
            }
            catch (RedisException ex)
            {
                LogRedisError(
                    "GetOrSet.ReleaseLock",
                    key,
                    ex,
                    $"Failed to release Redis cache lock for key '{key}'.");
            }
        }
    }

    public async Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _database.KeyDeleteAsync(BuildKey(key));
        }
        catch (RedisException ex)
        {
            LogRedisError(
                "Delete",
                key,
                ex,
                $"Failed to delete Redis cache key '{key}'.");

            return false;
        }
    }

    public async Task<bool> DeletePrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var pattern = $"{BuildKey(prefix)}*";
            var endpoints = _redis.GetEndPoints();
            var deletedAny = false;

            foreach (var endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var server = _redis.GetServer(endpoint);

                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                var batch = new List<RedisKey>(256);

                foreach (var redisKey in server.Keys(
                             database: _database.Database,
                             pattern: pattern,
                             pageSize: 500))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    batch.Add(redisKey);

                    if (batch.Count >= 256)
                    {
                        var deletedCount = await _database.KeyDeleteAsync(
                            batch.ToArray());

                        deletedAny |= deletedCount > 0;
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    var deletedCount = await _database.KeyDeleteAsync(
                        batch.ToArray());

                    deletedAny |= deletedCount > 0;
                }
            }

            return deletedAny;
        }
        catch (RedisException ex)
        {
            LogRedisError(
                "DeletePrefix",
                prefix,
                ex,
                $"Failed to delete Redis cache keys with prefix '{prefix}'.");

            return false;
        }
    }

    private void LogRedisError(
        string operationName,
        string key,
        RedisException exception,
        string message)
    {
        using var op = OperationLogger.Start(
            _logger,
            $"RedisCacheService.{operationName}",
            ("CacheKey", key));

        op.Error($"{message} Exception: {exception}");
    }

    private void LogCacheError(
        string operationName,
        string key,
        JsonException exception,
        string message)
    {
        using var op = OperationLogger.Start(
            _logger,
            $"RedisCacheService.{operationName}",
            ("CacheKey", key));

        op.Error($"{message} Exception: {exception}");
    }

    private static string BuildKey(string key) =>
        $"{CachePrefix}:{key}";
}
