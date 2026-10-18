
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Mova.Application.Interfaces.Caching;

namespace Mova.Infrastructure.Caching;

public sealed class CacheInvalidationInterceptor : SaveChangesInterceptor
{
    private readonly ICacheService _cacheService;
    private readonly ILogger<CacheInvalidationInterceptor> _logger;

    private readonly ConcurrentDictionary<DbContext, string[]> _pendingPrefixes = new();

    public CacheInvalidationInterceptor(
        ICacheService cacheService,
        ILogger<CacheInvalidationInterceptor> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;

        if (context is null)
        {
            return base.SavingChangesAsync(
                eventData, result, cancellationToken);
        }

        var prefixes = context.ChangeTracker
            .Entries()
            .Where(entry => entry.State is
                EntityState.Added or
                EntityState.Modified or
                EntityState.Deleted)
            .SelectMany(entry =>
                EntityCacheInvalidationMap.GetPrefixes(
                    entry.Metadata.ClrType))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (prefixes.Length > 0)
        {
            _pendingPrefixes[context] = prefixes;
        }
        else
        {
            _pendingPrefixes.TryRemove(context, out _);
        }

        return base.SavingChangesAsync(
            eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (eventData.Context is { } context &&
                _pendingPrefixes.TryRemove(context, out var prefixes))
            {
                foreach (var prefix in prefixes)
                {
                    try
                    {
                        await _cacheService.DeletePrefixAsync(
                            prefix,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to invalidate cache prefix {CachePrefix} after saving changes.",
                            prefix);
                    }
                }
            }
        }
        finally
        {
            await base.SavedChangesAsync(
                eventData, result, cancellationToken);
        }

        return result;
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            _pendingPrefixes.TryRemove(context, out _);
        }

        return base.SaveChangesFailedAsync(
            eventData,
            cancellationToken);
    }
}
