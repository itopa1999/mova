using System.Net;
using Moq;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using SchedulePreviewQuery = Mova.Application.BBL.Queries.AccountWallet.GetWalletSchedulePreviewQuery;

namespace Mova.Tests.Handlers;

public sealed class GetWalletSchedulePreviewQueryTests : BaseTest
{
    private const string UserPublicId = "user_schedule_preview";
    private const string OtherUserPublicId = "someone_else";

    private readonly Mock<IWalletRuleService> _walletRuleService = new();

    private SchedulePreviewQuery.Handler CreateHandler()
    {
        return new SchedulePreviewQuery.Handler(
            UnitOfWork,
            _walletRuleService.Object);
    }

    private SchedulePreviewQuery.Query CreateQuery(
        long walletId = 1,
        string? userPublicId = null)
    {
        return new SchedulePreviewQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
            WalletId = walletId,
        };
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

    private void SetupNextReleaseSequence(
        params (DateTimeOffset ScheduledFor, decimal Naira)[] sequence)
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

    private void SetupNextReleaseReturnsNull()
    {
        _walletRuleService
            .Setup(x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((NextWalletRelease?)null);
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
        decimal targetNaira = 1000m,
        decimal lockedNaira = 0m,
        decimal releasedNaira = 0m,
        WalletStatus status = WalletStatus.Active)
    {
        if (categoryId is null || categoryId <= 0)
            throw new InvalidOperationException("Category required.");

        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            Name = name,
            CategoryId = categoryId.Value,
            TargetAmount = Money.FromNaira(targetNaira),
            AvailableAmount = Money.FromNaira(0),
            LockedAmount = Money.FromNaira(lockedNaira),
            FundedAmount = Money.FromNaira(0),
            TotalReleasedAmount = Money.FromNaira(releasedNaira),
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
        var result = await handler.Handle(CreateQuery(userPublicId: ""), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User ID is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownWallet_ReturnsNotFound()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: 999_999), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Wallet not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithWalletOwnedByOtherUser_ReturnsNotFound()
    {
        var cat = await SeedCategoryAsync();
        var otherWallet = await SeedWalletAsync(OtherUserPublicId, "Theirs", cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: otherWallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Wallet not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithNoStoredReleasesAndNoRule_ReturnsEmptyReleases()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 0m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Empty(result.Data!.Releases);
    }

    [Fact]
    public async Task Handle_MapsWalletAggregates()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id,
            targetNaira: 5000m,
            releasedNaira: 1200m,
            lockedNaira: 800m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(wallet.Id, result.Data!.WalletId);
        Assert.Equal(5000m, result.Data.TargetAmount);
        Assert.Equal(1200m, result.Data.TotalReleasedAmount);
        Assert.Equal(800m, result.Data.RemainingLockedAmount);
    }

    [Fact]
    public async Task Handle_StoredReleases_AreMarkedNotProjectedAndCarryIds()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        var scheduledFor = DateTimeOffset.UtcNow.AddDays(3);
        var release = await SeedReleaseAsync(
            wallet.Id, rule.Id, 100m, scheduledFor, null, ReleaseStatus.Scheduled);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Releases);
        Assert.Equal(release.Id, item.ScheduledReleaseId);
        Assert.False(item.IsProjected);
        Assert.False(item.IsReleased);
        Assert.Equal(scheduledFor, item.ScheduledFor);
        Assert.Equal(100m, item.Amount);
    }

    [Fact]
    public async Task Handle_ReleasedRow_IsMarkedIsReleased()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        var at = DateTimeOffset.UtcNow.AddDays(-2);
        await SeedReleaseAsync(wallet.Id, rule.Id, 100m, at, at, ReleaseStatus.Released);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Releases);
        Assert.True(item.IsReleased);
        Assert.Equal(at, item.ReleasedAt);
    }

    [Fact]
    public async Task Handle_StoredReleases_AreOrderedByScheduledForThenId()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        var t1 = DateTimeOffset.UtcNow.AddDays(-10);
        var t2 = DateTimeOffset.UtcNow.AddDays(-5);
        var t3 = DateTimeOffset.UtcNow.AddDays(-1);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m, t2, null, ReleaseStatus.Scheduled);
        await SeedReleaseAsync(wallet.Id, rule.Id, 200m, t3, null, ReleaseStatus.Scheduled);
        await SeedReleaseAsync(wallet.Id, rule.Id, 300m, t1, null, ReleaseStatus.Scheduled);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var amounts = result.Data!.Releases.Select(r => r.Amount).ToList();
        Assert.Equal(new[] { 300m, 100m, 200m }, amounts);
    }

    [Fact]
    public async Task Handle_NoRule_DoesNotProjectAnyReleases()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 500m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Empty(result.Data!.Releases);
        _walletRuleService.Verify(
            x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithRuleButZeroLocked_DoesNotProject()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 0m);
        await SeedWalletRuleAsync(wallet.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Empty(result.Data!.Releases);
        _walletRuleService.Verify(
            x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithRuleAndLocked_ProjectsProjectedRows()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 50m);
        await SeedWalletRuleAsync(wallet.Id);

        var projected = DateTimeOffset.UtcNow.AddDays(7);
        SetupNextRelease(projected, naira: 50m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Releases);
        Assert.Null(item.ScheduledReleaseId);
        Assert.True(item.IsProjected);
        Assert.False(item.IsReleased);
        Assert.Equal(50m, item.Amount);
        Assert.Equal(projected, item.ScheduledFor);
        Assert.Equal(nameof(ReleaseStatus.Scheduled), item.Status);
    }

    [Fact]
    public async Task Handle_ProjectionStopsWhenRemainingLockedIsExhausted()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 120m);
        await SeedWalletRuleAsync(wallet.Id);

        var baseDate = DateTimeOffset.UtcNow.AddDays(1);
        SetupNextReleaseSequence(
            (baseDate, 50m),
            (baseDate.AddDays(30), 50m),
            (baseDate.AddDays(60), 50m),
            (baseDate.AddDays(90), 50m));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(3, result.Data!.Releases.Count);
        Assert.Equal(120m, result.Data.Releases.Sum(r => r.Amount));
    }

    [Fact]
    public async Task Handle_ProjectionBreaksWhenServiceReturnsNull()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 500m);
        await SeedWalletRuleAsync(wallet.Id);

        SetupNextReleaseReturnsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Empty(result.Data!.Releases);
    }

    [Fact]
    public async Task Handle_ProjectionSubtractsAlreadyScheduledAmounts()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 200m);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(
            wallet.Id, rule.Id, 150m,
            DateTimeOffset.UtcNow.AddDays(1), null, ReleaseStatus.Scheduled);

        var projected = DateTimeOffset.UtcNow.AddDays(30);
        SetupNextRelease(projected, naira: 50m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(2, result.Data!.Releases.Count);
        Assert.Equal(200m, result.Data.Releases.Sum(r => r.Amount));
    }

    [Fact]
    public async Task Handle_Projection_IsCappedByRemainingAmount()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 200m);
        await SeedWalletRuleAsync(wallet.Id);

        var baseDate = DateTimeOffset.UtcNow.AddDays(1);
        SetupNextReleaseSequence(
            (baseDate, 150m),
            (baseDate.AddDays(30), 150m));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(2, result.Data!.Releases.Count);
        Assert.Equal(200m, result.Data.Releases.Sum(r => r.Amount));
        Assert.Equal(150m, result.Data.Releases[0].Amount);
        Assert.Equal(50m, result.Data.Releases[1].Amount);
    }

    [Fact]
    public async Task Handle_Cursor_StartsFromLatestNonCancelledRelease()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 100m);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        var first = DateTimeOffset.UtcNow.AddDays(-10);
        var last = DateTimeOffset.UtcNow.AddDays(-1);
        var cancelled = DateTimeOffset.UtcNow.AddDays(5);

        await SeedReleaseAsync(wallet.Id, rule.Id, 20m, first, first, ReleaseStatus.Released);
        await SeedReleaseAsync(wallet.Id, rule.Id, 20m, last, null, ReleaseStatus.Scheduled);
        await SeedReleaseAsync(wallet.Id, rule.Id, 20m, cancelled, null, ReleaseStatus.Cancelled);

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        _walletRuleService.Verify(
            x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                last,
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
public async Task Handle_Cursor_FallsBackToRuleStartDateWhenNoStoredReleases()
{
    var cat = await SeedCategoryAsync();
    var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 100m);
    var startDate = DateTimeOffset.UtcNow.AddDays(-30);
    var rule = await SeedWalletRuleAsync(wallet.Id, startDate: startDate);

    SetupNextRelease(DateTimeOffset.UtcNow.AddDays(1), naira: 50m);

    var handler = CreateHandler();
    await handler.Handle(CreateQuery(walletId: wallet.Id), default);

    var expectedCursor = startDate.AddTicks(-1);
    _walletRuleService.Verify(
        x => x.GetNextReleaseAsync(
            It.Is<WalletRule>(r => r.Id == rule.Id),
            expectedCursor,
            It.IsAny<CancellationToken>()),
        Times.AtLeastOnce);
}

    [Fact]
    public async Task Handle_FinalList_IsOrderedByScheduledFor()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, lockedNaira: 100m);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        var t1 = DateTimeOffset.UtcNow.AddDays(-5);
        var t2 = DateTimeOffset.UtcNow.AddDays(-1);
        await SeedReleaseAsync(wallet.Id, rule.Id, 10m, t2, null, ReleaseStatus.Scheduled);
        await SeedReleaseAsync(wallet.Id, rule.Id, 20m, t1, null, ReleaseStatus.Scheduled);

        SetupNextRelease(DateTimeOffset.UtcNow.AddDays(10), naira: 30m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var ordered = result.Data!.Releases.Select(r => r.ScheduledFor).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i] >= ordered[i - 1]);
        }
    }
}