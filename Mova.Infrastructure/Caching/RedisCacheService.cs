using System.Text.Json;
using Mova.Application.Interfaces.Caching;
using StackExchange.Redis;

namespace Mova.Infrastructure.Caching;

public sealed class RedisCacheService : ICacheService
{
    private const string CachePrefix = "mova:cache";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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
        return await callback(cancellationToken);
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> callback,
        TimeSpan? timeout = null,
        TimeSpan? lockTimeout = null,
        TimeSpan? maxWait = null,
        CancellationToken cancellationToken = default)
    {
        return await callback(cancellationToken);
    }

    public Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<bool> DeletePrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    private static string BuildKey(string key) => $"{CachePrefix}:{key}";
}