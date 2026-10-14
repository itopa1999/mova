using System.Net;
using Moq;
using Mova.Application.BBL.Queries.WalletTemplates;
using Mova.Application.Interfaces.Caching;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;
using Xunit;
using ListWalletTemplatesQuery = Mova.Application.BBL.Queries.WalletTemplates.ListWalletTemplatesQuery;

namespace Mova.Tests.Handlers;

public sealed class ListWalletTemplatesQueryTests : BaseTest
{
    private readonly Mock<ICacheService> _cache = new();

    private ListWalletTemplatesQuery.Handler CreateHandler()
    {
        return new ListWalletTemplatesQuery.Handler(
            UnitOfWork,
            _cache.Object);
    }

    private ListWalletTemplatesQuery.Query CreateQuery(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        long? categoryId = null)
    {
        return new ListWalletTemplatesQuery.Query
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            CategoryId = categoryId,
        };
    }

    private void SetupCacheMiss()
    {
        _cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<ListWalletTemplatesQuery.WalletTemplateDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                (string _,
                 Func<CancellationToken, Task<List<ListWalletTemplatesQuery.WalletTemplateDto>>> factory,
                 TimeSpan? _,
                 CancellationToken ct) => factory(ct));
    }

    private void SetupCacheHit(List<ListWalletTemplatesQuery.WalletTemplateDto> cached)
    {
        _cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<ListWalletTemplatesQuery.WalletTemplateDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);
    }

    private void SetupCacheReturnsNull()
    {
        _cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<ListWalletTemplatesQuery.WalletTemplateDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ListWalletTemplatesQuery.WalletTemplateDto>?)null!);
    }

    private void SetupCacheThrows()
    {
        _cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<ListWalletTemplatesQuery.WalletTemplateDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated cache failure"));
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

    private async Task<WalletTemplate> SeedTemplateAsync(
        long categoryId,
        string name = "Template",
        string description = "A template",
        bool isActive = true,
        int sortOrder = 0,
        int usageCount = 0,
        decimal targetNaira = 1000m,
        decimal releaseNaira = 100m,
        string iconName = "Star",
        string[]? tags = null,
        ReleaseFrequency frequency = ReleaseFrequency.Monthly,
        string frequencyConfig = "{}",
        PayoutDestination destination = PayoutDestination.Bank)
    {
        var template = new WalletTemplate
        {
            CategoryId = categoryId,
            Name = name,
            Description = description,
            IsActive = isActive,
            SortOrder = sortOrder,
            UsageCount = usageCount,
            DefaultTargetAmount = Money.FromNaira(targetNaira),
            DefaultReleaseAmount = Money.FromNaira(releaseNaira),
            IconName = iconName,
            Tags = tags ?? Array.Empty<string>(),
            DefaultFrequency = frequency,
            DefaultFrequencyConfig = frequencyConfig,
            DefaultPayoutDestination = destination,
        };

        await UnitOfWork.AddAsync(template);
        await UnitOfWork.SaveChangesAsync();
        return template;
    }

    [Fact]
    public async Task Handle_OnCacheMiss_LoadsActiveTemplatesFromDb()
    {
        var cat = await SeedCategoryAsync();
        var t1 = await SeedTemplateAsync(cat.Id, "Rent", sortOrder: 1);
        var t2 = await SeedTemplateAsync(cat.Id, "Savings", sortOrder: 2);

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.Equal(t1.Id, result.Data.Items[0].Id);
        Assert.Equal(t2.Id, result.Data.Items[1].Id);
    }

    [Fact]
    public async Task Handle_ExcludesInactiveTemplates()
    {
        var cat = await SeedCategoryAsync();
        await SeedTemplateAsync(cat.Id, "Active", isActive: true);
        await SeedTemplateAsync(cat.Id, "Inactive", isActive: false);

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Active", item.Name);
    }

    [Fact]
    public async Task Handle_OrdersBySortOrderThenId()
    {
        var cat = await SeedCategoryAsync();
        var high = await SeedTemplateAsync(cat.Id, "High", sortOrder: 10);
        var low = await SeedTemplateAsync(cat.Id, "Low", sortOrder: 1);
        var mid = await SeedTemplateAsync(cat.Id, "Mid", sortOrder: 5);

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(
            new[] { low.Id, mid.Id, high.Id },
            result.Data!.Items.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Handle_FiltersByCategoryId()
    {
        var catA = await SeedCategoryAsync("Rent", "Home");
        var catB = await SeedCategoryAsync("Savings", "PiggyBank");
        await SeedTemplateAsync(catA.Id, "A-template");
        await SeedTemplateAsync(catB.Id, "B-template");

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(categoryId: catA.Id), default);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("A-template", item.Name);
    }

    [Fact]
    public async Task Handle_Search_MatchesNameCaseInsensitively()
    {
        var cat = await SeedCategoryAsync();
        await SeedTemplateAsync(cat.Id, "Rent Fund");
        await SeedTemplateAsync(cat.Id, "Emergency");

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(search: "RENT"), default);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Rent Fund", item.Name);
    }

    [Fact]
    public async Task Handle_Search_MatchesDescription()
    {
        var cat = await SeedCategoryAsync();
        await SeedTemplateAsync(cat.Id, "A", description: "Save for vacation");
        await SeedTemplateAsync(cat.Id, "B", description: "Monthly bill");

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(search: "vacation"), default);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("A", item.Name);
    }

    [Fact]
    public async Task Handle_SearchNoMatches_ReturnsEmptyPage()
    {
        var cat = await SeedCategoryAsync();
        await SeedTemplateAsync(cat.Id, "Rent");

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(search: "nonexistent"), default);

        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.TotalCount);
        Assert.Equal(0, result.Data.TotalPages);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectSliceAndMetadata()
    {
        var cat = await SeedCategoryAsync();
        for (var i = 0; i < 7; i++)
        {
            await SeedTemplateAsync(cat.Id, $"T{i}", sortOrder: i);
        }

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(page: 2, pageSize: 3),
            default);

        Assert.Equal(7, result.Data!.TotalCount);
        Assert.Equal(3, result.Data.TotalPages);
        Assert.Equal(2, result.Data.Page);
        Assert.Equal(3, result.Data.PageSize);
        Assert.Equal(3, result.Data.Items.Count);
    }

    [Fact]
    public async Task Handle_PageSizeOver100_ClampsTo100()
    {
        var cat = await SeedCategoryAsync();
        await SeedTemplateAsync(cat.Id);

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(pageSize: 5_000), default);

        Assert.Equal(100, result.Data!.PageSize);
    }

    [Fact]
    public async Task Handle_InvalidPageAndPageSize_FallBackToDefaults()
    {
        var cat = await SeedCategoryAsync();
        await SeedTemplateAsync(cat.Id);

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(page: 0, pageSize: 0), default);

        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(20, result.Data.PageSize);
    }

    [Fact]
    public async Task Handle_MapsCategoryNameAndIcon()
    {
        var cat = await SeedCategoryAsync("Emergency", "ShieldAlert");
        await SeedTemplateAsync(cat.Id, "Rainy day");

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Emergency", item.CategoryName);
        Assert.Equal("ShieldAlert", item.CategoryIcon);
    }

    [Fact]
    public async Task Handle_MapsAllTemplateFields()
    {
        var cat = await SeedCategoryAsync();
        await SeedTemplateAsync(
            cat.Id,
            name: "Rent",
            description: "Monthly rent",
            targetNaira: 2000m,
            releaseNaira: 250m,
            iconName: "HomeIcon",
            tags: new[]{ "essential" },
            frequency: ReleaseFrequency.Weekly,
            frequencyConfig: "{\"dayOfWeek\":1}",
            destination: PayoutDestination.Wallet,
            sortOrder: 5,
            usageCount: 42);

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("Rent", item.Name);
        Assert.Equal("Monthly rent", item.Description);
        Assert.Equal(2000m, item.DefaultTargetAmount);
        Assert.Equal(250m, item.DefaultReleaseAmount);
        Assert.Equal(nameof(ReleaseFrequency.Weekly), item.DefaultFrequency);
        Assert.Equal("{\"dayOfWeek\":1}", item.DefaultFrequencyConfig);
        Assert.Equal("wallet", item.DefaultPayoutDestination);
        Assert.Equal("HomeIcon", item.IconName);
        Assert.Contains("essential", item.Tags);
        Assert.Equal(5, item.SortOrder);
        Assert.Equal(42, item.UsageCount);
    }

    [Fact]
    public async Task Handle_UsesWalletTemplatesCacheKey()
    {
        SetupCacheMiss();

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(), default);

        _cache.Verify(
            x => x.GetOrSetFastAsync(
                CacheKeys.WalletTemplates(),
                It.IsAny<Func<CancellationToken, Task<List<ListWalletTemplatesQuery.WalletTemplateDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UsesTwelveHourCacheTtl()
    {
        SetupCacheMiss();

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(), default);

        _cache.Verify(
            x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<ListWalletTemplatesQuery.WalletTemplateDto>>>>(),
                TimeSpan.FromHours(12),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheReturnsNull_ReturnsEmptyPage()
    {
        SetupCacheReturnsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.TotalCount);
    }

    [Fact]
    public async Task Handle_WhenCacheThrows_ReturnsInternalServerError()
    {
        SetupCacheThrows();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal(
            "An error occurred while retrieving wallet templates. Please try again later.",
            result.Message);
    }

    [Fact]
    public async Task Handle_OnCacheHit_SkipsDatabase()
    {
        var cached = new List<ListWalletTemplatesQuery.WalletTemplateDto>
        {
            new() { Id = 1, Name = "From cache" },
        };

        SetupCacheHit(cached);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("From cache", item.Name);
    }
}