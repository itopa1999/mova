using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.BBL.Queries.BanksAccount;
using Mova.Application.BBL.Queries.SchedulePreview;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;
using CategoriesQuery = Mova.Application.BBL.Queries.AccountWallet.GetWalletCategories;
using FlagsQuery = Mova.Application.BBL.Queries.Admin.GetFeatureFlags;
using ScheduleQuery = Mova.Application.BBL.Queries.SchedulePreview.SchedulePreviewQuery;
using TemplatesQuery = Mova.Application.BBL.Queries.WalletTemplates.ListWalletTemplatesQuery;

namespace Mova.Tests.Handlers;

public sealed class StaticQueryResponseTests : BaseTest
{
    [Fact]
    public async Task GetWalletCategories_MapsAvailableCategoriesInStableOrder()
    {
        await UnitOfWork.AddAsync(new WalletCategory
        {
            Name = "Travel",
            Icon = "Plane"
        });
        await UnitOfWork.AddAsync(new WalletCategory
        {
            Name = "Savings",
            Icon = "PiggyBank"
        });
        await UnitOfWork.SaveChangesAsync();

        var cache = new Mock<ICacheService>();
        cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<CategoriesQuery.WalletCategoryDto>?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                string key,
                Func<CancellationToken, Task<List<CategoriesQuery.WalletCategoryDto>?>> callback,
                TimeSpan? timeout,
                CancellationToken cancellationToken) =>
                callback(cancellationToken));

        var handler = new CategoriesQuery.Handler(UnitOfWork, cache.Object);
        var result = await handler.Handle(
            new CategoriesQuery.Query(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(new[] { "Travel", "Savings" }, result.Data!.Select(x => x.Name));
        Assert.Equal(new[] { "Plane", "PiggyBank" }, result.Data.Select(x => x.Icon));
    }

    [Fact]
    public async Task ListWalletTemplates_FiltersAndMapsCachedTemplates()
    {
        var templates = new List<TemplatesQuery.WalletTemplateDto>
        {
            new()
            {
                Id = 1,
                Name = "Rent plan",
                Description = "Monthly housing goal",
                CategoryId = 4,
                CategoryName = "Housing",
                CategoryIcon = "Home",
                DefaultTargetAmount = 50_000m,
                DefaultReleaseAmount = 5_000m,
                DefaultFrequency = "Monthly",
                DefaultFrequencyConfig = "{\"day\":1}",
                DefaultPayoutDestination = "Bank",
                IconName = "House",
                Tags = ["rent", "monthly"],
                SortOrder = 1
            },
            new()
            {
                Id = 2,
                Name = "Travel plan",
                Description = "Trip savings",
                CategoryId = 5,
                CategoryName = "Travel",
                CategoryIcon = "Plane",
                Tags = ["trip"],
                SortOrder = 2
            }
        };
        var cache = new Mock<ICacheService>();
        cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<TemplatesQuery.WalletTemplateDto>?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);
        var handler = new TemplatesQuery.Handler(UnitOfWork, cache.Object);

        var result = await handler.Handle(
            new TemplatesQuery.Query
            {
                CategoryId = 4,
                Search = "rent"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data!.TotalCount);
        var template = Assert.Single(result.Data.Items);
        Assert.Equal("Rent plan", template.Name);
        Assert.Equal("Housing", template.CategoryName);
        Assert.Equal(50_000m, template.DefaultTargetAmount);
        Assert.Equal(new[] { "rent", "monthly" }, template.Tags);
    }

    [Fact]
    public async Task GetBanks_ForwardsSearchAndReturnsProviderResults()
    {
        var banks = new List<BankDto>
        {
            new() { Name = "Example Bank", Code = "001", Slug = "example-bank" }
        };
        var service = new Mock<IBankService>();
        service
            .Setup(x => x.GetAllBanksAsync("Example"))
            .ReturnsAsync(banks);
        var handler = new GetBanks.Handler(service.Object);

        var result = await handler.Handle(
            new GetBanks.Query { Name = "Example" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(banks[0], Assert.Single(result.Data!.Banks));
        service.Verify(x => x.GetAllBanksAsync("Example"), Times.Once);
    }

    [Fact]
    public async Task SchedulePreview_MapsServiceResultAndSampleDates()
    {
        var date = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
        var preview = new SchedulePreviewResult
        {
            IsSuccess = true,
            Description = "Weekly saving plan",
            TargetAmount = 1_000m,
            ReleaseAmount = 250m,
            RegularReleaseAmount = 250m,
            FinalReleaseAmount = 250m,
            TotalReleases = 4,
            TotalAmount = 1_000m,
            FirstReleaseDate = date,
            ComputedEndDate = date.AddDays(21),
            FrequencyType = ReleaseFrequency.Weekly,
            SampleReleaseDates =
            [
                new ReleaseDatePreview
                {
                    Date = date,
                    Amount = 250m,
                    ReleaseNumber = 1,
                    CumulativeAmount = 250m
                }
            ]
        };
        var service = new Mock<ISchedulePreviewService>();
        service
            .Setup(x => x.PreviewScheduleAsync(
                1_000m,
                250m,
                ReleaseFrequency.Weekly,
                "{}",
                date,
                4,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(preview);
        var handler = new ScheduleQuery.Handler(
            service.Object,
            NullLogger<ScheduleQuery.Handler>.Instance);

        var result = await handler.Handle(
            new ScheduleQuery.Query
            {
                TargetAmount = 1_000m,
                ReleaseAmount = 250m,
                FrequencyType = ReleaseFrequency.Weekly,
                FrequencyConfig = "{}",
                StartDate = date,
                MaxReleases = 4
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(1_000m, result.Data!.TotalAmount);
        Assert.Equal(4, result.Data.TotalReleases);
        var release = Assert.Single(result.Data.SampleReleaseDates);
        Assert.Equal(date, release.Date);
        Assert.Equal(250m, release.CumulativeAmount);
    }

    [Fact]
    public async Task GetFeatureFlags_MapsAllFeatureSnapshotFields()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var service = new Mock<IFeatureFlagService>();
        service
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new IFeatureFlagService.FeatureFlagSnapshot(
                    7,
                    FeatureFlagName.AllowDepositFunds,
                    "Allow deposits",
                    true,
                    "{\"limit\":1000}",
                    createdAt,
                    null)
            ]);
        var handler = new FlagsQuery.Handler(service.Object);

        var result = await handler.Handle(
            new FlagsQuery.Query(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var flag = Assert.Single(result.Data!);
        Assert.Equal(7, flag.Id);
        Assert.Equal(FeatureFlagName.AllowDepositFunds, flag.Name);
        Assert.Equal("Allow deposits", flag.Description);
        Assert.True(flag.IsEnabled);
        Assert.Equal("{\"limit\":1000}", flag.Metadata);
        Assert.Equal(createdAt, flag.CreatedAt);
        Assert.Null(flag.ModifiedAt);
    }
}
