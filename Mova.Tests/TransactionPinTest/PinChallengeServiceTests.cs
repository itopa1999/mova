using Microsoft.EntityFrameworkCore;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Infrastructure.Authentication.Jwt;
using Xunit;

namespace Mova.Tests.PinChallengeTests;

public sealed class PinChallengeServiceTests : BaseTest
{
    private const string UserPublicId = "user-pin-challenge-test";
    private const string OtherUserPublicId = "other-user-pin-challenge-test";

    private PinChallengeService CreateService() => new(Context);

    private async Task<PinChallenge> SeedChallengeAsync(
        string userPublicId,
        PinChallengeType type,
        bool isUsed = false,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? usedAt = null)
    {
        var challenge = new PinChallenge
        {
            UserPublicId = userPublicId,
            PinChallengeType = type,
            IsUsed = isUsed,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(2),
            UsedAt = usedAt ?? default,
        };

        Context.PinChallenges.Add(challenge);
        await Context.SaveChangesAsync();
        return challenge;
    }

    // ---------------------------------------------------------
    // CreateAsync — happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithValidUser_ReturnsTrue()
    {
        var service = CreateService();

        var result = await service.CreateAsync(
            UserPublicId,
            PinChallengeType.CreateWallet);

        Assert.True(result);
    }

    [Fact]
    public async Task CreateAsync_PersistsChallengeToDatabase()
    {
        var service = CreateService();

        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);

        var stored = await Context.PinChallenges
            .SingleAsync(c => c.UserPublicId == UserPublicId);

        Assert.Equal(PinChallengeType.CreateWallet, stored.PinChallengeType);
        Assert.False(stored.IsUsed);
        Assert.Equal(default, stored.UsedAt);
    }

    [Fact]
    public async Task CreateAsync_SetsExpiresAtToRoughlyTwoMinutesFromNow()
    {
        var service = CreateService();
        var before = DateTimeOffset.UtcNow;

        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);

        var after = DateTimeOffset.UtcNow;
        var stored = await Context.PinChallenges
            .SingleAsync(c => c.UserPublicId == UserPublicId);

        var lowerBound = before.AddMinutes(2).AddSeconds(-5);
        var upperBound = after.AddMinutes(2).AddSeconds(5);

        Assert.InRange(stored.ExpiresAt, lowerBound, upperBound);
    }

    [Fact]
    public async Task CreateAsync_ForDifferentChallengeTypes_CreatesSeparateRows()
    {
        var service = CreateService();

        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);
        await service.CreateAsync(UserPublicId, PinChallengeType.BreakWallet);

        var count = await Context.PinChallenges
            .CountAsync(c => c.UserPublicId == UserPublicId);

        Assert.Equal(2, count);
    }

    // ---------------------------------------------------------
    // CreateAsync — invalidation of prior challenges
    // ---------------------------------------------------------

    [Fact]
    public async Task CreateAsync_InvalidatesPriorActiveChallengeOfSameType()
    {
        var existing = await SeedChallengeAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        var service = CreateService();
        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);

        var reloaded = await Context.PinChallenges
            .AsNoTracking()
            .SingleAsync(c => c.Id == existing.Id);

        Assert.True(reloaded.IsUsed);
        Assert.NotEqual(default, reloaded.UsedAt);
    }

    [Fact]
    public async Task CreateAsync_LeavesOtherChallengeTypesUntouched()
    {
        var otherType = await SeedChallengeAsync(
            UserPublicId, PinChallengeType.BreakWallet);

        var service = CreateService();
        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);

        var reloaded = await Context.PinChallenges
            .AsNoTracking()
            .SingleAsync(c => c.Id == otherType.Id);

        Assert.False(reloaded.IsUsed);
        Assert.Equal(default, reloaded.UsedAt);
    }

    [Fact]
    public async Task CreateAsync_LeavesOtherUsersChallengesUntouched()
    {
        var otherUser = await SeedChallengeAsync(
            OtherUserPublicId, PinChallengeType.CreateWallet);

        var service = CreateService();
        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);

        var reloaded = await Context.PinChallenges
            .AsNoTracking()
            .SingleAsync(c => c.Id == otherUser.Id);

        Assert.False(reloaded.IsUsed);
    }

    [Fact]
    public async Task CreateAsync_LeavesAlreadyUsedChallengesUntouched()
    {
        var alreadyUsedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var used = await SeedChallengeAsync(
            UserPublicId,
            PinChallengeType.CreateWallet,
            isUsed: true,
            usedAt: alreadyUsedAt);

        var service = CreateService();
        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);

        var reloaded = await Context.PinChallenges
            .AsNoTracking()
            .SingleAsync(c => c.Id == used.Id);

        Assert.True(reloaded.IsUsed);
        Assert.Equal(alreadyUsedAt, reloaded.UsedAt);
    }

    [Fact]
    public async Task CreateAsync_LeavesExpiredChallengesUntouched()
    {
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expired = await SeedChallengeAsync(
            UserPublicId,
            PinChallengeType.CreateWallet,
            expiresAt: expiredAt);

        var service = CreateService();
        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);

        var reloaded = await Context.PinChallenges
            .AsNoTracking()
            .SingleAsync(c => c.Id == expired.Id);

        Assert.False(reloaded.IsUsed);
        Assert.Equal(default, reloaded.UsedAt);
    }

    // ---------------------------------------------------------
    // ValidateAsync — happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_WithActiveChallenge_ReturnsSuccess()
    {
        await SeedChallengeAsync(UserPublicId, PinChallengeType.CreateWallet);
        var service = CreateService();

        var (success, message) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.True(success);
        Assert.Equal(string.Empty, message);
    }

    [Fact]
    public async Task ValidateAsync_ConsumesTheChallenge()
    {
        var challenge = await SeedChallengeAsync(
            UserPublicId, PinChallengeType.CreateWallet);
        var service = CreateService();

        await service.ValidateAsync(UserPublicId, PinChallengeType.CreateWallet);

        var reloaded = await Context.PinChallenges
            .AsNoTracking()
            .SingleAsync(c => c.Id == challenge.Id);

        Assert.True(reloaded.IsUsed);
        Assert.NotEqual(default, reloaded.UsedAt);
    }

    [Fact]
    public async Task ValidateAsync_CannotBeConsumedTwice()
    {
        await SeedChallengeAsync(UserPublicId, PinChallengeType.CreateWallet);
        var service = CreateService();

        var (firstSuccess, _) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);
        var (secondSuccess, secondMessage) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.True(firstSuccess);
        Assert.False(secondSuccess);
        Assert.Equal("Please verify your PIN again.", secondMessage);
    }

    [Fact]
    public async Task ValidateAsync_WithMultipleActive_ConsumesMostRecent()
    {
        var older = await SeedChallengeAsync(
            UserPublicId,
            PinChallengeType.CreateWallet,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(1));

        var newer = await SeedChallengeAsync(
            UserPublicId,
            PinChallengeType.CreateWallet,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

        var service = CreateService();
        var (success, _) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.True(success);

        var olderReloaded = await Context.PinChallenges
            .AsNoTracking().SingleAsync(c => c.Id == older.Id);
        var newerReloaded = await Context.PinChallenges
            .AsNoTracking().SingleAsync(c => c.Id == newer.Id);

        Assert.False(olderReloaded.IsUsed);
        Assert.True(newerReloaded.IsUsed);
    }

    // ---------------------------------------------------------
    // ValidateAsync — failure cases
    // ---------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_WithNoChallenge_ReturnsFailure()
    {
        var service = CreateService();

        var (success, message) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.False(success);
        Assert.Equal("Please verify your PIN again.", message);
    }

    [Fact]
    public async Task ValidateAsync_WithExpiredChallenge_ReturnsFailure()
    {
        await SeedChallengeAsync(
            UserPublicId,
            PinChallengeType.CreateWallet,
            expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        var service = CreateService();
        var (success, message) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.False(success);
        Assert.Equal("Please verify your PIN again.", message);
    }

    [Fact]
    public async Task ValidateAsync_WithUsedChallenge_ReturnsFailure()
    {
        await SeedChallengeAsync(
            UserPublicId,
            PinChallengeType.CreateWallet,
            isUsed: true,
            usedAt: DateTimeOffset.UtcNow.AddSeconds(-5));

        var service = CreateService();
        var (success, _) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.False(success);
    }

    [Fact]
    public async Task ValidateAsync_WithWrongChallengeType_ReturnsFailure()
    {
        await SeedChallengeAsync(UserPublicId, PinChallengeType.CreateWallet);
        var service = CreateService();

        var (success, message) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.BreakWallet);

        Assert.False(success);
        Assert.Equal("Please verify your PIN again.", message);
    }

    [Fact]
    public async Task ValidateAsync_WithDifferentUser_ReturnsFailure()
    {
        await SeedChallengeAsync(UserPublicId, PinChallengeType.CreateWallet);
        var service = CreateService();

        var (success, _) = await service.ValidateAsync(
            OtherUserPublicId, PinChallengeType.CreateWallet);

        Assert.False(success);
    }

    [Fact]
    public async Task ValidateAsync_DoesNotConsumeChallengeForWrongType()
    {
        var challenge = await SeedChallengeAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        var service = CreateService();
        await service.ValidateAsync(UserPublicId, PinChallengeType.BreakWallet);

        var reloaded = await Context.PinChallenges
            .AsNoTracking().SingleAsync(c => c.Id == challenge.Id);

        Assert.False(reloaded.IsUsed);
    }

    // ---------------------------------------------------------
    // Interaction between Create and Validate
    // ---------------------------------------------------------

    [Fact]
    public async Task CreateThenValidate_Succeeds()
    {
        var service = CreateService();

        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);
        var (success, _) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.True(success);
    }

    [Fact]
    public async Task CreateTwiceThenValidate_OnlyLatestIsValid()
    {
        var service = CreateService();

        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);
        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);

        var (success, _) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.True(success);

        var (secondSuccess, _) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.False(secondSuccess);
    }

    [Fact]
    public async Task CreateAfterValidate_IssuesFreshChallenge()
    {
        var service = CreateService();

        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);
        var (firstSuccess, _) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);
        Assert.True(firstSuccess);

        await service.CreateAsync(UserPublicId, PinChallengeType.CreateWallet);
        var (secondSuccess, _) = await service.ValidateAsync(
            UserPublicId, PinChallengeType.CreateWallet);

        Assert.True(secondSuccess);
    }

    // ---------------------------------------------------------
    // Concurrency
    // ---------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_ConcurrentRequests_OnlyOneSucceeds()
    {
        await SeedChallengeAsync(UserPublicId, PinChallengeType.CreateWallet);
        var service = CreateService();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => service.ValidateAsync(
                UserPublicId, PinChallengeType.CreateWallet))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r.Success);

        Assert.Equal(1, successCount);
    }
}