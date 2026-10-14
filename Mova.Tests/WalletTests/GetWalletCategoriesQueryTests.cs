using System.Net;
using Moq;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.Interfaces.Caching;
using Mova.Domain.Entities;
using Mova.Shared.Constants;
using Xunit;
using GetWalletCategoriesQuery = Mova.Application.BBL.Queries.AccountWallet.GetWalletCategories;

namespace Mova.Tests.Handlers;

public sealed class GetWalletCategoriesQueryTests : BaseTest
{
    private readonly Mock<ICacheService> _cache = new();

    private GetWalletCategoriesQuery.Handler CreateHandler()
    {
        return new GetWalletCategoriesQuery.Handler(
            UnitOfWork,
            _cache.Object);
    }

    private void SetupCacheMiss()
    {
        _cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<GetWalletCategoriesQuery.WalletCategoryDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                (string _,
                 Func<CancellationToken, Task<List<GetWalletCategoriesQuery.WalletCategoryDto>>> factory,
                 TimeSpan? _,
                 CancellationToken ct) => factory(ct));
    }

    private void SetupCacheHit(List<GetWalletCategoriesQuery.WalletCategoryDto> cached)
    {
        _cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<GetWalletCategoriesQuery.WalletCategoryDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);
    }

    private void SetupCacheReturnsNull()
    {
        _cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<GetWalletCategoriesQuery.WalletCategoryDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<GetWalletCategoriesQuery.WalletCategoryDto>?)null!);
    }

    private void SetupCacheThrows()
    {
        _cache
            .Setup(x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<GetWalletCategoriesQuery.WalletCategoryDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated cache failure"));
    }

    private async Task<WalletCategory> SeedCategoryAsync(
        string name,
        string? icon = null)
    {
        var category = new WalletCategory { Name = name, Icon = icon };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();
        return category;
    }

    [Fact]
    public async Task Handle_OnCacheMiss_LoadsFromDbAndReturnsCategories()
    {
        var cat1 = await SeedCategoryAsync("Savings", "PiggyBank");
        var cat2 = await SeedCategoryAsync("Rent", "Home");

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetWalletCategoriesQuery.Query(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Wallet categories retrieved successfully.", result.Message);

        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal(cat1.Id, result.Data[0].Id);
        Assert.Equal("Savings", result.Data[0].Name);
        Assert.Equal("PiggyBank", result.Data[0].Icon);
        Assert.Equal(cat2.Id, result.Data[1].Id);
        Assert.Equal("Rent", result.Data[1].Name);
        Assert.Equal("Home", result.Data[1].Icon);
    }

    [Fact]
    public async Task Handle_OnCacheMiss_WithNoCategories_ReturnsEmptyList()
    {
        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetWalletCategoriesQuery.Query(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task Handle_OnCacheHit_ReturnsCachedValueWithoutHittingDb()
    {
        // No categories seeded — if the handler hits the DB, the result will be empty.
        var cached = new List<GetWalletCategoriesQuery.WalletCategoryDto>
        {
            new() { Id = 100, Name = "Cached A", Icon = "IconA" },
            new() { Id = 200, Name = "Cached B", Icon = "IconB" },
        };

        SetupCacheHit(cached);

        var handler = CreateHandler();
        var result = await handler.Handle(new GetWalletCategoriesQuery.Query(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal(100, result.Data[0].Id);
        Assert.Equal("Cached A", result.Data[0].Name);
        Assert.Equal(200, result.Data[1].Id);
        Assert.Equal("Cached B", result.Data[1].Name);
    }

    [Fact]
    public async Task Handle_OnCacheMiss_OrdersCategoriesByIdAscending()
    {
        var cat1 = await SeedCategoryAsync("A", "IconA");
        var cat2 = await SeedCategoryAsync("B", "IconB");
        var cat3 = await SeedCategoryAsync("C", "IconC");

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetWalletCategoriesQuery.Query(), default);

        Assert.NotNull(result.Data);
        var ids = result.Data!.Select(x => x.Id).ToList();
        Assert.Equal(new[] { cat1.Id, cat2.Id, cat3.Id }, ids);
    }

    [Fact]
    public async Task Handle_WithNullIcon_MapsToEmptyString()
    {
        var cat = await SeedCategoryAsync("No icon category", icon: null);

        SetupCacheMiss();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetWalletCategoriesQuery.Query(), default);

        Assert.NotNull(result.Data);
        var dto = Assert.Single(result.Data!);
        Assert.Equal(cat.Id, dto.Id);
        Assert.Equal("No icon category", dto.Name);
        Assert.Equal(string.Empty, dto.Icon);
    }

    [Fact]
    public async Task Handle_UsesExpectedCacheKey()
    {
        SetupCacheMiss();

        var handler = CreateHandler();
        await handler.Handle(new GetWalletCategoriesQuery.Query(), default);

        _cache.Verify(
            x => x.GetOrSetFastAsync(
                CacheKeys.WalletCategories(),
                It.IsAny<Func<CancellationToken, Task<List<GetWalletCategoriesQuery.WalletCategoryDto>>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UsesTwelveHourCacheTtl()
    {
        SetupCacheMiss();

        var handler = CreateHandler();
        await handler.Handle(new GetWalletCategoriesQuery.Query(), default);

        _cache.Verify(
            x => x.GetOrSetFastAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<List<GetWalletCategoriesQuery.WalletCategoryDto>>>>(),
                TimeSpan.FromHours(12),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheReturnsNull_FallsBackToEmptyList()
    {
        SetupCacheReturnsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetWalletCategoriesQuery.Query(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task Handle_WhenCacheThrows_ReturnsInternalServerError()
    {
        SetupCacheThrows();

        var handler = CreateHandler();
        var result = await handler.Handle(new GetWalletCategoriesQuery.Query(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal(
            "An error occurred while retrieving wallet categories. Please try again later.",
            result.Message);
        Assert.Null(result.Data);
    }
}