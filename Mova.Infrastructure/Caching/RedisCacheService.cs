using System.Text.Json;
using Mova.Application.Interfaces.Caching;
using StackExchange.Redis;

namespace Mova.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private const string CachePrefix = "mova:cache";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
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
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = BuildKey(key);

        bool found = false;
        T? cached = default;

        try
        {
            var raw = await _database.StringGetAsync(fullKey);
            if (raw.HasValue)
            {
                cached = JsonSerializer.Deserialize<T>(raw!, SerializerOptions);
                found = true;
            }
        }
        catch (RedisException)
        {
        }
        catch (JsonException)
        {
        }

        if (found)
        {
            return cached;
        }

        var value = await callback(cancellationToken);

        if (value is not null)
        {
            try
            {
                var serialized = JsonSerializer.Serialize(value, SerializerOptions);
                await _database.StringSetAsync(fullKey, serialized, timeout ?? DefaultTimeout);
            }
            catch (RedisException)
            {
            }
            catch (JsonException)
            {
            }
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

        bool found = false;
        T? cached = default;

        try
        {
            var raw = await _database.StringGetAsync(fullKey);
            if (raw.HasValue)
            {
                cached = JsonSerializer.Deserialize<T>(raw!, SerializerOptions);
                found = true;
            }
        }
        catch (RedisException)
        {
        }
        catch (JsonException)
        {
        }

        if (found)
        {
            return cached;
        }

        var lockKey = $"{fullKey}:lock";
        var lockValue = Guid.NewGuid().ToString("N");
        var lockTtl = lockTimeout ?? DefaultLockTimeout;
        var waitFor = maxWait ?? DefaultMaxWait;

        bool acquired;
        try
        {
            acquired = await _database.StringSetAsync(
                lockKey, lockValue, lockTtl, When.NotExists);
        }
        catch (RedisException)
        {
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
                        var retry = JsonSerializer.Deserialize<T>(raw!, SerializerOptions);
                        return retry;
                    }
                }
                catch (RedisException)
                {
                }
                catch (JsonException)
                {
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
                    var serialized = JsonSerializer.Serialize(value, SerializerOptions);
                    await _database.StringSetAsync(fullKey, serialized, timeout ?? DefaultTimeout);
                }
                catch (RedisException)
                {
                }
                catch (JsonException)
                {
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
            catch (RedisException)
            {
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
        catch (RedisException)
        {
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
                var server = _redis.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                var batch = new List<RedisKey>(256);
                foreach (var redisKey in server.Keys(pattern: pattern, pageSize: 500))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    batch.Add(redisKey);

                    if (batch.Count >= 256)
                    {
                        await _database.KeyDeleteAsync(batch.ToArray());
                        deletedAny = true;
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    await _database.KeyDeleteAsync(batch.ToArray());
                    deletedAny = true;
                }
            }

            return deletedAny;
        }
        catch (RedisException)
        {
            return false;
        }
    }

    private static string BuildKey(string key) => $"{CachePrefix}:{key}";
}