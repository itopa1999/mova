using System.Net;
using Moq;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using GetAllWalletsQuery = Mova.Application.BBL.Queries.AccountWallet.GetAllWallets;

namespace Mova.Tests.Handlers;

public sealed class GetAllWalletsQueryTests : BaseTest
{
    private const string UserPublicId = "user_wallets_test";
    private const string OtherUserPublicId = "someone_else";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";

    private readonly Mock<IIdentityService> _identityService = new();

    private GetAllWalletsQuery.Handler CreateHandler()
    {
        return new GetAllWalletsQuery.Handler(
            UnitOfWork,
            _identityService.Object);
    }

    private GetAllWalletsQuery.Query CreateQuery(
        string? userPublicId = null,
        int page = 1,
        int pageSize = 10,
        string? search = null)
    {
        return new GetAllWalletsQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
            Page = page,
            PageSize = pageSize,
            Search = search,
        };
    }

    private void SetupUserExists(
        decimal balanceNaira = 0m,
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
                Money.FromNaira(balanceNaira),
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
        string? description = null,
        decimal targetNaira = 500m,
        decimal lockedNaira = 100m,
        WalletStatus status = WalletStatus.Active)
    {
        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            Name = name,
            Description = description,
            CategoryId = categoryId ?? 0,
            TargetAmount = Money.FromNaira(targetNaira),
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

    private async Task<RenewalPolicy> SeedRenewalPolicyAsync(
        long walletId,
        RenewalStatus status = RenewalStatus.Active,
        bool isEnabled = true,
        RenewalTriggerType triggerType = RenewalTriggerType.OnCompletion,
        RefillAmountType refillAmountType = RefillAmountType.Fixed,
        decimal refillNaira = 500m)
    {
        var policy = new RenewalPolicy
        {
            WalletId = walletId,
            Status = status,
            IsEnabled = isEnabled,
            TriggerType = triggerType,
            RefillAmountType = refillAmountType,
            RefillAmount = Money.FromNaira(refillNaira),
        };

        await UnitOfWork.AddAsync(policy);
        await UnitOfWork.SaveChangesAsync();
        return policy;
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
    public async Task Handle_WithNoWallets_ReturnsEmptyPage()
    {
        SetupUserExists();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.TotalCount);
        Assert.Equal(0m, result.Data.TotalControlledAmount);
        Assert.Equal(0, result.Data.ActiveWalletCount);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyWalletsOwnedByRequestingUser()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();

        await SeedWalletAsync(UserPublicId, "Owner Active", cat.Id, status: WalletStatus.Active);
        await SeedWalletAsync(UserPublicId, "Owner Paused", cat.Id, status: WalletStatus.Paused);
        await SeedWalletAsync(OtherUserPublicId, "Other Active", cat.Id, status: WalletStatus.Active);
        await SeedWalletAsync(OtherUserPublicId, "Other Paused", cat.Id, status: WalletStatus.Paused);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data!.TotalCount);
        Assert.Equal(2, result.Data.Items.Count);
        Assert.All(result.Data.Items, w => Assert.StartsWith("Owner", w.Name));
        Assert.DoesNotContain(result.Data.Items, w => w.Name.StartsWith("Other"));
    }

    [Fact]
    public async Task Handle_TotalsReflectOnlyRequestingUsersWallets()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();

        await SeedWalletAsync(UserPublicId, "Owner 1", cat.Id, lockedNaira: 100m);
        await SeedWalletAsync(UserPublicId, "Owner 2", cat.Id, lockedNaira: 200m);
        await SeedWalletAsync(UserPublicId, "Owner Paused", cat.Id,
            lockedNaira: 50m, status: WalletStatus.Paused);
        await SeedWalletAsync(OtherUserPublicId, "Other 1", cat.Id,
            lockedNaira: 9_999m, status: WalletStatus.Active);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(350m, result.Data!.TotalControlledAmount);
        Assert.Equal(2, result.Data.ActiveWalletCount);
    }

    [Fact]
    public async Task Handle_SearchByName_IsCaseInsensitiveAndUserScoped()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(UserPublicId, "Travel Fund", cat.Id);
        await SeedWalletAsync(UserPublicId, "Savings", cat.Id);
        await SeedWalletAsync(OtherUserPublicId, "Travel Other", cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(search: "TRAVEL"),
            default);

        Assert.True(result.IsSuccess);
        var wallet = Assert.Single(result.Data!.Items);
        Assert.Equal("Travel Fund", wallet.Name);
    }

    [Fact]
    public async Task Handle_SearchByStatus_MatchesEnumName()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(UserPublicId, "Alpha", cat.Id, status: WalletStatus.Active);
        await SeedWalletAsync(UserPublicId, "Beta", cat.Id, status: WalletStatus.Paused);
        await SeedWalletAsync(UserPublicId, "Gamma", cat.Id, status: WalletStatus.Completed);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(search: "paused"),
            default);

        Assert.True(result.IsSuccess);
        var wallet = Assert.Single(result.Data!.Items);
        Assert.Equal("Beta", wallet.Name);
    }

    [Fact]
    public async Task Handle_SearchNeverLeaksMatchingWalletsFromOtherUsers()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(UserPublicId, "Shared name", cat.Id);
        await SeedWalletAsync(OtherUserPublicId, "Shared name", cat.Id);
        await SeedWalletAsync(OtherUserPublicId, "Shared name 2", cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(search: "Shared name"),
            default);

        Assert.True(result.IsSuccess);
        var wallet = Assert.Single(result.Data!.Items);
        Assert.Equal("Shared name", wallet.Name);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectMetadata()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        for (var i = 1; i <= 5; i++)
            await SeedWalletAsync(UserPublicId, $"W{i}", cat.Id);

        await SeedWalletAsync(OtherUserPublicId, "Other1", cat.Id);
        await SeedWalletAsync(OtherUserPublicId, "Other2", cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(page: 2, pageSize: 2),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Data!.TotalCount);
        Assert.Equal(3, result.Data.TotalPages);
        Assert.Equal(2, result.Data.Page);
        Assert.Equal(2, result.Data.PageSize);
        Assert.Equal(2, result.Data.Items.Count);
    }

    [Fact]
    public async Task Handle_PageSizeOver100_ClampsTo100()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(UserPublicId, "W1", cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(pageSize: 5_000),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Data!.PageSize);
    }

    [Fact]
    public async Task Handle_InvalidPageAndPageSize_FallBackToDefaults()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(UserPublicId, "W1", cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(page: 0, pageSize: 0),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(10, result.Data.PageSize);
    }

    [Fact]
    public async Task Handle_HasAutomationIsTrueOnlyForWalletsWithRenewalPolicy()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();

        var withPolicy = await SeedWalletAsync(UserPublicId, "With automation", cat.Id);
        var withoutPolicy = await SeedWalletAsync(UserPublicId, "Without automation", cat.Id);

        await SeedRenewalPolicyAsync(withPolicy.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);

        var withAuto = result.Data!.Items.Single(w => w.Name == "With automation");
        var withoutAuto = result.Data.Items.Single(w => w.Name == "Without automation");

        Assert.True(withAuto.HasAutomation);
        Assert.Equal(nameof(RenewalStatus.Active), withAuto.AutomationStatus);

        Assert.False(withoutAuto.HasAutomation);
        Assert.Null(withoutAuto.AutomationStatus);
    }

    [Fact]
    public async Task Handle_AutomationPolicyBelongingToOtherUsersWallet_DoesNotLeakIntoPolicies()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();

        var mine = await SeedWalletAsync(UserPublicId, "Mine", cat.Id);
        var theirs = await SeedWalletAsync(OtherUserPublicId, "Theirs", cat.Id);

        await SeedRenewalPolicyAsync(theirs.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        var dto = Assert.Single(result.Data!.Items);
        Assert.Equal("Mine", dto.Name);
        Assert.False(dto.HasAutomation);
        Assert.Null(dto.AutomationStatus);
    }

    [Fact]
    public async Task Handle_IncludesCategoryNameAndIcon()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync("Emergency", "ShieldAlert");
        await SeedWalletAsync(UserPublicId, "Rainy day", cat.Id, targetNaira: 50_000m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        var wallet = Assert.Single(result.Data!.Items);
        Assert.Equal("Emergency", wallet.CategoryName);
        Assert.Equal("ShieldAlert", wallet.CategoryIcon);
        Assert.Equal("Rainy day", wallet.Name);
        Assert.Equal(50_000m, wallet.TargetAmount);
        Assert.Equal(nameof(WalletStatus.Active), wallet.Status);
    }
}