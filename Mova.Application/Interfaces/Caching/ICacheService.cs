namespace Mova.Application.Interfaces.Caching;

public interface ICacheService
{
    Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> callback,
        TimeSpan? timeout = null,
        TimeSpan? lockTimeout = null,
        TimeSpan? maxWait = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<bool> DeletePrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default);
}
