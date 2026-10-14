using System.Net;
using Moq;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class GetReleasesQueryTests : BaseTest
{
    private const string UserPublicId = "user_releases_test";
    private const string OtherUserPublicId = "someone_else";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IWalletRuleService> _walletRuleService = new();

    private GetReleasesQuery.Handler CreateHandler()
    {
        return new GetReleasesQuery.Handler(
            UnitOfWork,
            _identityService.Object,
            _walletRuleService.Object);
    }

    private GetReleasesQuery.Query CreateQuery(
        string? userPublicId = null,
        int upcomingLimit = 3)
    {
        return new GetReleasesQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
            UpcomingLimit = upcomingLimit,
        };
    }

    private void SetupUserExists(
        string publicId = UserPublicId,
        string email = UserEmail,
        string firstName = UserFirstName)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                UserId,
                publicId,
                firstName,
                null,
                "Starboy",
                email,
                "08050000000",
                null,
                Money.FromNaira(0),
                string.Empty,
                true,
                true,
                true,
                false,
                string.Empty,
                DateTimeOffset.UtcNow));
    }

    private void SetupUserNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);
    }

    private void SetupNextRelease(DateTimeOffset scheduledFor, decimal naira = 50m)
    {
        _walletRuleService
            .Setup(x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NextWalletRelease
            {
                ScheduledFor = scheduledFor,
                Amount = Money.FromNaira(naira),
            });
    }

    private void SetupNextReleaseSequence(params (DateTimeOffset ScheduledFor, decimal Naira)[] sequence)
    {
        var setup = _walletRuleService
            .SetupSequence(x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()));

        foreach (var (scheduledFor, naira) in sequence)
        {
            setup = setup.ReturnsAsync(new NextWalletRelease
            {
                ScheduledFor = scheduledFor,
                Amount = Money.FromNaira(naira),
            });
        }
    }

    private async Task<WalletCategory> SeedCategoryAsync(
        string name = "Savings",
        string icon = "PiggyBank")
    {
        var category = new WalletCategory { Name = name, Icon = icon };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();
        return category;
    }

    private async Task<Wallet> SeedWalletAsync(
        string userPublicId = UserPublicId,
        string name = "Rent",
        long? categoryId = null,
        decimal lockedNaira = 100m,
        WalletStatus status = WalletStatus.Active)
    {
        if (categoryId is null || categoryId <= 0)
            throw new InvalidOperationException(
                "Wallet.CategoryId is required and must reference a persisted WalletCategory.");

        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            Name = name,
            CategoryId = categoryId.Value,
            TargetAmount = Money.FromNaira(1000m),
            AvailableAmount = Money.FromNaira(0),
            LockedAmount = Money.FromNaira(lockedNaira),
            FundedAmount = Money.FromNaira(lockedNaira),
            TotalReleasedAmount = Money.FromNaira(0),
            TotalWithdrawnAmount = Money.FromNaira(0),
            Status = status,
        };

        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();
        return wallet;
    }

    private async Task<WalletRule> SeedWalletRuleAsync(
        long walletId,
        decimal amountNaira = 50m,
        ReleaseFrequency frequency = ReleaseFrequency.Monthly,
        string frequencyConfig = "{\"dayOfMonth\":1}",
        DateTimeOffset? startDate = null)
    {
        var start = startDate ?? DateTimeOffset.UtcNow.AddDays(-1);

        var rule = new WalletRule
        {
            WalletId = walletId,
            Amount = Money.FromNaira(amountNaira),
            Frequency = frequency,
            FrequencyConfig = frequencyConfig,
            StartDate = start,
            EndDate = start.AddYears(1),
        };

        await UnitOfWork.AddAsync(rule);
        await UnitOfWork.SaveChangesAsync();
        return rule;
    }

    private async Task<ScheduledRelease> SeedReleaseAsync(
        long walletId,
        long walletRuleId,
        decimal amountNaira,
        DateTimeOffset scheduledFor,
        DateTimeOffset? releasedAt,
        ReleaseStatus status)
    {
        var release = new ScheduledRelease
        {
            WalletId = walletId,
            WalletRuleId = walletRuleId,
            Amount = Money.FromNaira(amountNaira),
            ScheduledFor = scheduledFor,
            ReleasedAt = releasedAt,
            Status = status,
            FailedAttempts = 0,
        };

        await UnitOfWork.AddAsync(release);
        await UnitOfWork.SaveChangesAsync();
        return release;
    }

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateQuery(userPublicId: ""),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User public ID is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_DoesNotQueryIdentity()
    {
        var handler = CreateHandler();

        await handler.Handle(CreateQuery(userPublicId: ""), default);

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsNotFound()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithNoWallets_ReturnsEmptyCollections()
    {
        SetupUserExists();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.TodayReleased);
        Assert.Empty(result.Data.Scheduled);
        Assert.Empty(result.Data.Upcoming);
    }

    [Fact]
    public async Task Handle_TodayReleased_IncludesOnlyReleasesFromToday()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(name: "Rent", categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m,
            scheduledFor: DateTimeOffset.UtcNow,
            releasedAt: DateTimeOffset.UtcNow,
            status: ReleaseStatus.Released);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var item = Assert.Single(result.Data!.TodayReleased);
        Assert.Equal("Rent", item.WalletName);
        Assert.Equal(100m, item.Amount);
        Assert.Equal(nameof(ReleaseStatus.Released), item.Status);
        Assert.False(item.IsProjected);
    }

    [Fact]
    public async Task Handle_TodayReleased_ExcludesYesterday()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m,
            scheduledFor: DateTimeOffset.UtcNow.AddDays(-1),
            releasedAt: DateTimeOffset.UtcNow.AddDays(-1),
            status: ReleaseStatus.Released);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Empty(result.Data!.TodayReleased);
    }

    [Fact]
    public async Task Handle_TodayReleased_ExcludesNonReleasedStatuses()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m,
            scheduledFor: DateTimeOffset.UtcNow,
            releasedAt: DateTimeOffset.UtcNow,
            status: ReleaseStatus.Paused);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m,
            scheduledFor: DateTimeOffset.UtcNow,
            releasedAt: null,
            status: ReleaseStatus.Scheduled);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m,
            scheduledFor: DateTimeOffset.UtcNow,
            releasedAt: DateTimeOffset.UtcNow,
            status: ReleaseStatus.Failed);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Empty(result.Data!.TodayReleased);
    }

    [Fact]
    public async Task Handle_TodayReleased_ExcludesOtherUsersReleases()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();

        var mine = await SeedWalletAsync(UserPublicId, "Mine", cat.Id);
        var theirs = await SeedWalletAsync(OtherUserPublicId, "Theirs", cat.Id);

        var myRule = await SeedWalletRuleAsync(mine.Id);
        var theirRule = await SeedWalletRuleAsync(theirs.Id);

        await SeedReleaseAsync(mine.Id, myRule.Id, 100m,
            scheduledFor: DateTimeOffset.UtcNow,
            releasedAt: DateTimeOffset.UtcNow,
            status: ReleaseStatus.Released);

        await SeedReleaseAsync(theirs.Id, theirRule.Id, 999m,
            scheduledFor: DateTimeOffset.UtcNow,
            releasedAt: DateTimeOffset.UtcNow,
            status: ReleaseStatus.Released);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var item = Assert.Single(result.Data!.TodayReleased);
        Assert.Equal(100m, item.Amount);
        Assert.Equal("Mine", item.WalletName);
    }

    [Fact]
    public async Task Handle_Scheduled_ReturnsOnlyScheduledStatus()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m,
            scheduledFor: DateTimeOffset.UtcNow.AddDays(3),
            releasedAt: null,
            status: ReleaseStatus.Scheduled);

        await SeedReleaseAsync(wallet.Id, rule.Id, 200m,
            scheduledFor: DateTimeOffset.UtcNow,
            releasedAt: DateTimeOffset.UtcNow,
            status: ReleaseStatus.Released);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var item = Assert.Single(result.Data!.Scheduled);
        Assert.Equal(100m, item.Amount);
        Assert.Equal(nameof(ReleaseStatus.Scheduled), item.Status);
        Assert.False(item.IsProjected);
    }

    [Fact]
    public async Task Handle_Scheduled_ExcludesOtherUsersReleases()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();

        var mine = await SeedWalletAsync(UserPublicId, "Mine", cat.Id);
        var theirs = await SeedWalletAsync(OtherUserPublicId, "Theirs", cat.Id);
        var myRule = await SeedWalletRuleAsync(mine.Id);
        var theirRule = await SeedWalletRuleAsync(theirs.Id);

        await SeedReleaseAsync(mine.Id, myRule.Id, 100m,
            scheduledFor: DateTimeOffset.UtcNow.AddDays(1),
            releasedAt: null,
            status: ReleaseStatus.Scheduled);

        await SeedReleaseAsync(theirs.Id, theirRule.Id, 999m,
            scheduledFor: DateTimeOffset.UtcNow.AddDays(1),
            releasedAt: null,
            status: ReleaseStatus.Scheduled);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var item = Assert.Single(result.Data!.Scheduled);
        Assert.Equal(100m, item.Amount);
        Assert.Equal("Mine", item.WalletName);
    }

    [Fact]
    public async Task Handle_Scheduled_OrdersByScheduledForAscending()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m,
            scheduledFor: DateTimeOffset.UtcNow.AddDays(5),
            releasedAt: null,
            status: ReleaseStatus.Scheduled);

        await SeedReleaseAsync(wallet.Id, rule.Id, 200m,
            scheduledFor: DateTimeOffset.UtcNow.AddDays(1),
            releasedAt: null,
            status: ReleaseStatus.Scheduled);

        await SeedReleaseAsync(wallet.Id, rule.Id, 300m,
            scheduledFor: DateTimeOffset.UtcNow.AddDays(3),
            releasedAt: null,
            status: ReleaseStatus.Scheduled);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var amounts = result.Data!.Scheduled.Select(s => s.Amount).ToList();
        Assert.Equal(new[] { 200m, 300m, 100m }, amounts);
    }

    [Fact]
    public async Task Handle_Upcoming_ExcludesWalletsWithoutRule()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 500m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Empty(result.Data!.Upcoming);
        _walletRuleService.Verify(
            x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Upcoming_ExcludesWalletsWithZeroLockedAmount()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 0m);
        await SeedWalletRuleAsync(wallet.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Empty(result.Data!.Upcoming);
        _walletRuleService.Verify(
            x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Upcoming_ReturnsProjectedItemsFromRuleService()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(name: "Rent", categoryId: cat.Id, lockedNaira: 50m);
        await SeedWalletRuleAsync(wallet.Id);

        var projectedAt = DateTimeOffset.UtcNow.AddDays(7);
        SetupNextRelease(projectedAt, naira: 50m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var item = Assert.Single(result.Data!.Upcoming);
        Assert.Equal("Rent", item.WalletName);
        Assert.Equal(50m, item.Amount);
        Assert.Null(item.ScheduledReleaseId);
        Assert.True(item.IsProjected);
        Assert.Equal(nameof(ReleaseStatus.Scheduled), item.Status);
        Assert.Equal(projectedAt, item.ScheduledFor);
    }

    [Fact]
    public async Task Handle_Upcoming_RespectsUpcomingLimit()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 150m);
        await SeedWalletRuleAsync(wallet.Id);

        var baseDate = DateTimeOffset.UtcNow.AddDays(1);
        SetupNextReleaseSequence(
            (baseDate, 50m),
            (baseDate.AddDays(30), 50m),
            (baseDate.AddDays(60), 50m),
            (baseDate.AddDays(90), 50m));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(upcomingLimit: 2), default);

        Assert.Equal(2, result.Data!.Upcoming.Count);
        Assert.All(result.Data.Upcoming, u => Assert.True(u.IsProjected));
    }

    [Fact]
    public async Task Handle_WhenQueryThrows_ReturnsInternalServerError()
    {
        SetupUserExists();

        var throwingUow = new Mock<IUnitOfWork>();
        throwingUow
            .Setup(x => x.Query<Wallet>())
            .Throws(new InvalidOperationException("Simulated DB failure"));

        var handler = new GetReleasesQuery.Handler(
            throwingUow.Object,
            _identityService.Object,
            _walletRuleService.Object);

        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal(
            "An error occurred while retrieving releases. Please try again later.",
            result.Message);
    }
}