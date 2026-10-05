using System.Net;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.BBL.Queries.SchedulePreview;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Xunit;
using ReleaseQuery = Mova.Application.BBL.MovaAPIs.GetReleasesQuery;

namespace Mova.Tests.Handlers;

public sealed class WalletQueryOwnershipTests : BaseTest
{
    private const string OwnerId = "wallet-query-owner";
    private const string OtherUserId = "wallet-query-other";

    [Fact]
    public async Task WalletDetailQueries_DoNotExposeAnotherUsersWallet()
    {
        var wallet = await SeedWalletAsync();
        var identity = new Mock<IIdentityService>();
        identity
            .Setup(x => x.GetByIdentifierAsync(
                OtherUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(OtherUserId));
        var walletRules = Mock.Of<IWalletRuleService>();

        var activities = await new GetWalletActivities.Handler(UnitOfWork).Handle(
            new GetWalletActivities.Query
            {
                UserPublicId = OtherUserId,
                WalletId = wallet.Id
            },
            CancellationToken.None);
        var payouts = await new GetWalletPayouts.Handler(UnitOfWork).Handle(
            new GetWalletPayouts.Query
            {
                UserPublicId = OtherUserId,
                WalletId = wallet.Id
            },
            CancellationToken.None);
        var bankAccount = await new GetWalletBankAccount.Handler(UnitOfWork).Handle(
            new GetWalletBankAccount.Query
            {
                UserPublicId = OtherUserId,
                WalletId = wallet.Id
            },
            CancellationToken.None);
        var schedule = await new GetWalletSchedulePreviewQuery.Handler(
            UnitOfWork,
            walletRules).Handle(
            new GetWalletSchedulePreviewQuery.Query
            {
                UserPublicId = OtherUserId,
                WalletId = wallet.Id
            },
            CancellationToken.None);
        var renewalEvents = await new GetRenewalEventsQuery.Handler(
            UnitOfWork,
            Mock.Of<ILogger<GetRenewalEventsQuery.Handler>>()).Handle(
            new GetRenewalEventsQuery.Query
            {
                UserPublicId = OtherUserId,
                WalletId = wallet.Id
            },
            CancellationToken.None);
        var renewalPolicy = await new GetRenewalPolicyQuery.Handler(
            UnitOfWork,
            Mock.Of<ILogger<GetRenewalPolicyQuery.Handler>>()).Handle(
            new GetRenewalPolicyQuery.Query
            {
                UserPublicId = OtherUserId,
                WalletId = wallet.Id
            },
            CancellationToken.None);
        var details = await new WalletDetails.Handler(
            UnitOfWork,
            identity.Object,
            Mock.Of<MediatR.IMediator>()).Handle(
            new WalletDetails.Query
            {
                UserPublicId = OtherUserId,
                WalletId = wallet.Id
            },
            CancellationToken.None);

        AssertNotFound(activities.StatusCode, activities.IsSuccess);
        AssertNotFound(payouts.StatusCode, payouts.IsSuccess);
        AssertNotFound(bankAccount.StatusCode, bankAccount.IsSuccess);
        AssertNotFound(schedule.StatusCode, schedule.IsSuccess);
        AssertNotFound(renewalEvents.StatusCode, renewalEvents.IsSuccess);
        AssertNotFound(renewalPolicy.StatusCode, renewalPolicy.IsSuccess);
        AssertNotFound(details.StatusCode, details.IsSuccess);
        identity.Verify(
            x => x.GetByIdentifierAsync(
                OtherUserId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WalletAnalyticsAndReleaseQueries_ReturnNoOtherUsersData()
    {
        await SeedWalletAsync();
        var identity = new Mock<IIdentityService>();
        identity
            .Setup(x => x.GetByIdentifierAsync(
                OtherUserId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(OtherUserId));

        var analytics = await new GetWalletAnalytics.Handler(UnitOfWork).Handle(
            new GetWalletAnalytics.Query
            {
                UserPublicId = OtherUserId,
                Date = new DateTime(2025, 1, 1)
            },
            CancellationToken.None);
        var releases = await new ReleaseQuery.Handler(
            UnitOfWork,
            identity.Object,
            Mock.Of<IWalletRuleService>()).Handle(
            new ReleaseQuery.Query { UserPublicId = OtherUserId },
            CancellationToken.None);

        Assert.True(analytics.IsSuccess);
        Assert.Equal(0m, analytics.Data!.MoneyProtected);
        Assert.Equal(0m, analytics.Data.MoneyReleased);
        Assert.True(releases.IsSuccess);
        Assert.Empty(releases.Data!.TodayReleased);
        Assert.Empty(releases.Data.Scheduled);
        Assert.Empty(releases.Data.Upcoming);
    }

    [Fact]
    public async Task WalletActivities_OnlyReturnTransactionsBelongingToTheWalletOwner()
    {
        var wallet = await SeedWalletAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedTransactionAsync(
            OwnerId,
            wallet.Id,
            "Owner release",
            TransactionType.Release,
            25m,
            now);
        await SeedTransactionAsync(
            OtherUserId,
            wallet.Id,
            "Foreign transaction attached to wallet",
            TransactionType.Withdrawal,
            900m,
            now.AddMinutes(1));

        var result = await new GetWalletActivities.Handler(UnitOfWork).Handle(
            new GetWalletActivities.Query
            {
                UserPublicId = OwnerId,
                WalletId = wallet.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data!.TotalActivities);
        var activity = Assert.Single(result.Data.Items).Activities.Single();
        Assert.Equal("Owner release", activity.Title);
        Assert.Equal(25m, activity.Amount);
        Assert.True(activity.IsCredit);
    }

    [Fact]
    public async Task WalletDetails_ReturnsExpectedDetailsForTheOwner()
    {
        var wallet = await SeedWalletAsync();
        var scheduledFor = DateTimeOffset.UtcNow.AddDays(2);
        var schedule = new GetWalletSchedulePreviewQuery.GetWalletSchedulePreviewResponseDto
        {
            WalletId = wallet.Id,
            TargetAmount = 500m,
            TotalReleasedAmount = 100m,
            RemainingLockedAmount = 300m,
            Releases =
            [
                new GetWalletSchedulePreviewQuery.ReleaseDto
                {
                    ScheduledFor = scheduledFor,
                    Amount = 100m,
                    Status = nameof(ReleaseStatus.Scheduled),
                    IsProjected = true
                }
            ]
        };
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(x => x.Send(
                It.IsAny<GetWalletSchedulePreviewQuery.Query>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BaseResult<GetWalletSchedulePreviewQuery.GetWalletSchedulePreviewResponseDto>(
                HttpStatusCode.OK,
                "Schedule preview",
                schedule));
        var identity = new Mock<IIdentityService>();
        identity
            .Setup(x => x.GetByIdentifierAsync(
                OwnerId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(OwnerId));

        var result = await new WalletDetails.Handler(
            UnitOfWork,
            identity.Object,
            mediator.Object).Handle(
            new WalletDetails.Query
            {
                UserPublicId = OwnerId,
                WalletId = wallet.Id
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(wallet.Id, result.Data!.WalletId);
        Assert.Equal("Private wallet", result.Data.Name);
        Assert.Equal("Savings", result.Data.CategoryName);
        Assert.Equal(500m, result.Data.TargetAmount);
        Assert.Equal(300m, result.Data.LockedAmount);
        Assert.Equal(40m, result.Data.ProgressPercentage);
        Assert.Equal(scheduledFor, result.Data.NextReleaseDate);
        Assert.True(Assert.Single(result.Data.SchedulePreview).IsProjected);
    }

    [Fact]
    public async Task WalletAnalytics_ExcludeTransactionsWhoseUserDoesNotOwnTheWallet()
    {
        var wallet = await SeedWalletAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedTransactionAsync(
            OwnerId,
            wallet.Id,
            "Owner release",
            TransactionType.Release,
            25m,
            now);
        await SeedTransactionAsync(
            OtherUserId,
            wallet.Id,
            "Foreign release",
            TransactionType.Release,
            900m,
            now.AddMinutes(1));

        var result = await new GetWalletAnalytics.Handler(UnitOfWork).Handle(
            new GetWalletAnalytics.Query
            {
                UserPublicId = OwnerId,
                Date = DateTime.UtcNow
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(25m, result.Data!.MoneyReleased);
        Assert.Equal(500m, result.Data.MoneyProtected);
    }

    private async Task<Wallet> SeedWalletAsync()
    {
        var category = new WalletCategory
        {
            Name = "Savings",
            Icon = "PiggyBank"
        };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();

        var wallet = new Wallet
        {
            UserPublicId = OwnerId,
            CategoryId = category.Id,
            Name = "Private wallet",
            TargetAmount = Money.FromNaira(500m),
            LockedAmount = Money.FromNaira(300m),
            FundedAmount = Money.FromNaira(500m),
            TotalReleasedAmount = Money.FromNaira(100m),
            Status = WalletStatus.Active
        };
        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();
        return wallet;
    }

    private async Task SeedTransactionAsync(
        string userPublicId,
        long walletId,
        string title,
        TransactionType type,
        decimal amount,
        DateTimeOffset completedAt)
    {
        await UnitOfWork.AddAsync(new Transaction
        {
            UserPublicId = userPublicId,
            WalletId = walletId,
            Title = title,
            Amount = Money.FromNaira(amount),
            Type = type,
            Status = TransactionStatus.Completed,
            Reference = $"ref-{title.Replace(' ', '-')}",
            CompletedAt = completedAt
        });
        await UnitOfWork.SaveChangesAsync();
    }

    private static UserIdentityDto CreateIdentity(string publicId) =>
        new(
            99,
            publicId,
            "Other",
            null,
            "User",
            "other@example.com",
            "08050000001",
            null,
            Money.FromNaira(0m),
            string.Empty,
            false,
            false,
            false,
            false,
            string.Empty,
            DateTimeOffset.UtcNow);

    private static void AssertNotFound(HttpStatusCode statusCode, bool isSuccess)
    {
        Assert.False(isSuccess);
        Assert.Equal(HttpStatusCode.NotFound, statusCode);
    }
}
