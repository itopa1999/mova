using Mova.Application.Interfaces.Security;
using Mova.Shared.Constants;
using StackExchange.Redis;

namespace Mova.Infrastructure.Security;

public sealed class RedisTransactionPinAttemptStore : ITransactionPinAttemptStore
{
    private const int MaximumFailures = 3;
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan LockDuration = TimeSpan.FromHours(1);

    private const string RecordFailureScript = """
        local attempts = redis.call('INCR', KEYS[1])
        if attempts == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        if attempts >= tonumber(ARGV[2]) then
            redis.call('SET', KEYS[2], '1', 'PX', ARGV[3])
            return 1
        end
        return 0
        """;

    private const string ResetAfterSuccessfulVerificationScript = """
        if redis.call('EXISTS', KEYS[2]) == 1 then
            return 0
        end
        redis.call('DEL', KEYS[1])
        return 1
        """;

    private readonly IDatabase _database;

    public RedisTransactionPinAttemptStore(IConnectionMultiplexer redis)
    {
        _database = redis.GetDatabase();
    }

    public Task<bool> IsLockedAsync(
        string userPublicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _database.KeyExistsAsync(CacheKeys.TransactionPinLock(userPublicId));
    }

    public async Task<bool> RecordFailureAsync(
        string userPublicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _database.ScriptEvaluateAsync(
            RecordFailureScript,
            [
                CacheKeys.TransactionPinAttempts(userPublicId),
                CacheKeys.TransactionPinLock(userPublicId)
            ],
            [
                (long)AttemptWindow.TotalMilliseconds,
                MaximumFailures,
                (long)LockDuration.TotalMilliseconds
            ]);

        return (long)result == 1;
    }

    public async Task<bool> ResetAfterSuccessfulVerificationAsync(
        string userPublicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _database.ScriptEvaluateAsync(
            ResetAfterSuccessfulVerificationScript,
            [
                CacheKeys.TransactionPinAttempts(userPublicId),
                CacheKeys.TransactionPinLock(userPublicId)
            ]);

        return (long)result == 1;
    }

    public async Task ResetAsync(
        string userPublicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _database.KeyDeleteAsync(
        [
            CacheKeys.TransactionPinAttempts(userPublicId),
            CacheKeys.TransactionPinLock(userPublicId)
        ]);
    }
}
