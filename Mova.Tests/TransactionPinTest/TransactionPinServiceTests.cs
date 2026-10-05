using Microsoft.AspNetCore.Identity;
using Moq;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Security;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Services.Security;
using Xunit;

namespace Mova.Tests.TransactionPinTest;

public sealed class TransactionPinServiceTests : BaseTest
{
    private const string UserPublicId = "pin-lockout-user";
    private const string Pin = "123456";

    [Fact]
    public async Task VerifyPinAsync_LocksAfterThreeFailures()
    {
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            UserName = "pin-lockout@example.com",
            NormalizedUserName = "PIN-LOCKOUT@EXAMPLE.COM",
            Email = "pin-lockout@example.com",
            NormalizedEmail = "PIN-LOCKOUT@EXAMPLE.COM",
            PublicId = UserPublicId,
            FirstName = "Pin",
            LastName = "Lockout"
        };
        user.TransactionPinHash = hasher.HashPassword(user, Pin);

        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var attemptStore = new InMemoryPinAttemptStore();
        var service = new TransactionPinService(
            Context,
            hasher,
            Mock.Of<ICacheService>(),
            attemptStore);

        Assert.False(await service.VerifyPinAsync(UserPublicId, "000000"));
        Assert.False(await service.VerifyPinAsync(UserPublicId, "000000"));
        Assert.False(await service.VerifyPinAsync(UserPublicId, "000000"));
        Assert.True(attemptStore.IsLocked);
        Assert.False(await service.VerifyPinAsync(UserPublicId, Pin));
        Assert.Equal(3, attemptStore.FailureCount);
    }

    [Fact]
    public async Task VerifyPinAsync_ClearsFailuresAfterSuccessfulVerification()
    {
        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            UserName = "pin-reset@example.com",
            NormalizedUserName = "PIN-RESET@EXAMPLE.COM",
            Email = "pin-reset@example.com",
            NormalizedEmail = "PIN-RESET@EXAMPLE.COM",
            PublicId = UserPublicId,
            FirstName = "Pin",
            LastName = "Reset"
        };
        user.TransactionPinHash = hasher.HashPassword(user, Pin);

        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var attemptStore = new InMemoryPinAttemptStore();
        await attemptStore.RecordFailureAsync(UserPublicId);

        var service = new TransactionPinService(
            Context,
            hasher,
            Mock.Of<ICacheService>(),
            attemptStore);

        Assert.True(await service.VerifyPinAsync(UserPublicId, Pin));
        Assert.Equal(0, attemptStore.FailureCount);
        Assert.False(attemptStore.IsLocked);
    }

    private sealed class InMemoryPinAttemptStore : ITransactionPinAttemptStore
    {
        public int FailureCount { get; private set; }
        public bool IsLocked { get; private set; }

        public Task<bool> IsLockedAsync(
            string userPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(IsLocked);

        public Task<bool> RecordFailureAsync(
            string userPublicId,
            CancellationToken cancellationToken = default)
        {
            FailureCount++;
            IsLocked = FailureCount >= 3;
            return Task.FromResult(IsLocked);
        }

        public Task ResetAsync(
            string userPublicId,
            CancellationToken cancellationToken = default)
        {
            FailureCount = 0;
            IsLocked = false;
            return Task.CompletedTask;
        }
    }
}
