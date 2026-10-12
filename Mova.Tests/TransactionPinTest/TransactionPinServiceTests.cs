using Microsoft.AspNetCore.Identity;
using Moq;
using Mova.Application.Behaviors;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;
using Mova.Infrastructure.Identity;
using Mova.Infrastructure.Services.Security;
using Mova.Shared.Constants;

namespace Mova.Tests.TransactionPinTest;

public sealed class TransactionPinServiceTests : BaseTest
{
    private const string UserPublicId = "pin-lockout-user";
    private const string Pin = "123456";

    private readonly Mock<IPinChallenge> _pinChallengeMock = new();

    private TransactionPinService CreateService(
        IPasswordHasher<User> hasher,
        ITransactionPinAttemptStore attemptStore)
    {
        _pinChallengeMock
            .Setup(x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new TransactionPinService(
            Context,
            hasher,
            Mock.Of<ICacheService>(),
            attemptStore,
            _pinChallengeMock.Object);
    }

    [Fact]
    public void TransactionPinCacheKeys_UseRequestedRedisKeyFormats()
    {
        var prefix = CacheKeys.TransactionPinPrefixGlobal();

        Assert.Equal(
            $"{prefix}:attempts:{UserPublicId}",
            CacheKeys.TransactionPinAttempts(UserPublicId));
        Assert.Equal(
            $"{prefix}:lock:{UserPublicId}",
            CacheKeys.TransactionPinLock(UserPublicId));
    }

    [Fact]
    public async Task VerifyPinWithStatusAsync_LocksAfterThreeFailures()
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
        var service = CreateService(hasher, attemptStore);

        Assert.Equal(
            TransactionPinVerificationResult.Invalid,
            await service.VerifyPinWithStatusAsync(
                UserPublicId, "000000", PinChallengeType.CreateWallet));

        Assert.Equal(
            TransactionPinVerificationResult.Invalid,
            await service.VerifyPinWithStatusAsync(
                UserPublicId, "000000", PinChallengeType.CreateWallet));

        Assert.Equal(
            TransactionPinVerificationResult.LockedNow,
            await service.VerifyPinWithStatusAsync(
                UserPublicId, "000000", PinChallengeType.CreateWallet));

        Assert.True(attemptStore.IsLocked(UserPublicId));

        Assert.Equal(
            TransactionPinVerificationResult.Locked,
            await service.VerifyPinWithStatusAsync(
                UserPublicId, Pin, PinChallengeType.CreateWallet));

        Assert.Equal(3, attemptStore.FailureCount(UserPublicId));
    }

    [Fact]
    public async Task VerifyPinWithStatusAsync_ClearsFailuresAfterSuccessfulVerification()
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

        var service = CreateService(hasher, attemptStore);

        Assert.Equal(
            TransactionPinVerificationResult.Verified,
            await service.VerifyPinWithStatusAsync(
                UserPublicId, Pin, PinChallengeType.CreateWallet));

        Assert.Equal(0, attemptStore.FailureCount(UserPublicId));
        Assert.False(attemptStore.IsLocked(UserPublicId));
    }

    [Fact]
    public async Task VerifyPinWithStatusAsync_LockoutIsIsolatedPerUser()
    {
        var hasher = new PasswordHasher<User>();
        var firstUser = CreateUser(UserPublicId, "first-pin-user@example.com");
        var secondUser = CreateUser("another-pin-user", "second-pin-user@example.com");
        firstUser.TransactionPinHash = hasher.HashPassword(firstUser, Pin);
        secondUser.TransactionPinHash = hasher.HashPassword(secondUser, Pin);
        Context.Users.AddRange(firstUser, secondUser);
        await Context.SaveChangesAsync();

        var attemptStore = new InMemoryPinAttemptStore();
        var service = CreateService(hasher, attemptStore);

        await service.VerifyPinWithStatusAsync(
            UserPublicId, "000000", PinChallengeType.CreateWallet);
        await service.VerifyPinWithStatusAsync(
            UserPublicId, "000000", PinChallengeType.CreateWallet);
        await service.VerifyPinWithStatusAsync(
            UserPublicId, "000000", PinChallengeType.CreateWallet);

        Assert.True(attemptStore.IsLocked(UserPublicId));

        Assert.Equal(
            TransactionPinVerificationResult.Verified,
            await service.VerifyPinWithStatusAsync(
                secondUser.PublicId, Pin, PinChallengeType.CreateWallet));

        Assert.False(attemptStore.IsLocked(secondUser.PublicId));
    }

    [Fact]
    public async Task VerifyPinWithStatusAsync_DoesNotClearLockCreatedDuringSuccessfulVerification()
    {
        var hasher = new PasswordHasher<User>();
        var user = CreateUser(UserPublicId, "concurrent-pin-user@example.com");
        user.TransactionPinHash = hasher.HashPassword(user, Pin);
        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var attemptStore = new InMemoryPinAttemptStore
        {
            LockDuringSuccessfulReset = true
        };
        var service = CreateService(hasher, attemptStore);

        Assert.Equal(
            TransactionPinVerificationResult.Locked,
            await service.VerifyPinWithStatusAsync(
                UserPublicId, Pin, PinChallengeType.CreateWallet));

        Assert.True(attemptStore.IsLocked(UserPublicId));
        Assert.Equal(3, attemptStore.FailureCount(UserPublicId));
    }

    [Fact]
    public async Task VerifyPinWithStatusAsync_WhenChallengeCreationFails_ThrowsPinChallengePersistenceException()
    {
        var hasher = new PasswordHasher<User>();
        var user = CreateUser(UserPublicId, "challenge-fail@example.com");
        user.TransactionPinHash = hasher.HashPassword(user, Pin);
        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var attemptStore = new InMemoryPinAttemptStore();

        _pinChallengeMock
            .Setup(x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new TransactionPinService(
            Context,
            hasher,
            Mock.Of<ICacheService>(),
            attemptStore,
            _pinChallengeMock.Object);

        await Assert.ThrowsAsync<PinChallengePersistenceException>(
            () => service.VerifyPinWithStatusAsync(
                UserPublicId, Pin, PinChallengeType.CreateWallet));
    }

    [Fact]
    public async Task VerifyPinWithStatusAsync_ForwardsChallengeTypeToPinChallenge()
    {
        var hasher = new PasswordHasher<User>();
        var user = CreateUser(UserPublicId, "challenge-forward@example.com");
        user.TransactionPinHash = hasher.HashPassword(user, Pin);
        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var attemptStore = new InMemoryPinAttemptStore();

        _pinChallengeMock
            .Setup(x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = new TransactionPinService(
            Context,
            hasher,
            Mock.Of<ICacheService>(),
            attemptStore,
            _pinChallengeMock.Object);

        await service.VerifyPinWithStatusAsync(
            UserPublicId, Pin, PinChallengeType.BreakWallet);

        _pinChallengeMock.Verify(
            x => x.CreateAsync(
                UserPublicId,
                PinChallengeType.BreakWallet,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task VerifyPinWithStatusAsync_DoesNotCreateChallengeOnInvalidPin()
    {
        var hasher = new PasswordHasher<User>();
        var user = CreateUser(UserPublicId, "no-challenge-on-fail@example.com");
        user.TransactionPinHash = hasher.HashPassword(user, Pin);
        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        var attemptStore = new InMemoryPinAttemptStore();
        var service = CreateService(hasher, attemptStore);

        await service.VerifyPinWithStatusAsync(
            UserPublicId, "000000", PinChallengeType.CreateWallet);

        _pinChallengeMock.Verify(
            x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static User CreateUser(string publicId, string email) =>
        new()
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PublicId = publicId,
            FirstName = "Pin",
            LastName = "User"
        };

    private sealed class InMemoryPinAttemptStore : ITransactionPinAttemptStore
    {
        private readonly Dictionary<string, int> _failureCounts = new();

        public bool LockDuringSuccessfulReset { get; init; }

        public int FailureCount(string userPublicId) =>
            _failureCounts.GetValueOrDefault(userPublicId);

        public bool IsLocked(string userPublicId) =>
            FailureCount(userPublicId) >= 3;

        public Task<bool> IsLockedAsync(
            string userPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(IsLocked(userPublicId));

        public Task<bool> RecordFailureAsync(
            string userPublicId,
            CancellationToken cancellationToken = default)
        {
            _failureCounts[userPublicId] = FailureCount(userPublicId) + 1;
            return Task.FromResult(FailureCount(userPublicId) == 3);
        }

        public Task<bool> ResetAfterSuccessfulVerificationAsync(
            string userPublicId,
            CancellationToken cancellationToken = default)
        {
            if (LockDuringSuccessfulReset)
            {
                _failureCounts[userPublicId] = 3;
                return Task.FromResult(false);
            }

            if (IsLocked(userPublicId))
            {
                return Task.FromResult(false);
            }

            _failureCounts.Remove(userPublicId);
            return Task.FromResult(true);
        }

        public Task ResetAsync(
            string userPublicId,
            CancellationToken cancellationToken = default)
        {
            _failureCounts.Remove(userPublicId);
            return Task.CompletedTask;
        }
    }
}