using System.Net;
using Moq;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.BBL.Queries.Profile;
using Mova.Application.BBL.Queries.TransactionPin;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using WalletListQuery = Mova.Application.BBL.Queries.AccountWallet.GetAllWallets;

namespace Mova.Tests.Handlers;

public sealed class UserScopedQueryTests : BaseTest
{
    private const string UserPublicId = "query-owner";
    private const string OtherUserPublicId = "query-other";

    [Fact]
    public async Task GetAllWallets_ReturnsOnlyOwnerWalletsAndNormalizesPagination()
    {
        var category = new WalletCategory
        {
            Name = "Savings",
            Icon = "PiggyBank"
        };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();

        var ownerWallet = await SeedWalletAsync(category.Id, UserPublicId, "Owner wallet");
        await SeedWalletAsync(category.Id, UserPublicId, "Second owner wallet");
        await SeedWalletAsync(category.Id, OtherUserPublicId, "Other user's wallet");

        var identity = new Mock<IIdentityService>();
        identity
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(UserPublicId));
        var handler = new WalletListQuery.Handler(UnitOfWork, identity.Object);

        var result = await handler.Handle(
            new WalletListQuery.Query
            {
                UserPublicId = UserPublicId,
                Page = 0,
                PageSize = 0
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(10, result.Data.PageSize);
        Assert.Equal(2, result.Data.TotalCount);
        Assert.Equal(2, result.Data.Items.Count);
        Assert.Contains(result.Data.Items, x => x.WalletId == ownerWallet.Id);
        Assert.All(result.Data.Items, x => Assert.NotEqual("Other user's wallet", x.Name));
    }

    [Fact]
    public async Task GetProfile_ReturnsCompleteProfileIncludingCreatedAt()
    {
        var createdAt = new DateTimeOffset(2025, 4, 3, 2, 1, 0, TimeSpan.Zero);
        var identity = new Mock<IIdentityService>();
        identity
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(
                UserPublicId,
                createdAt: createdAt,
                transactionPinHash: "hashed-pin",
                notifyLoginAlerts: true,
                notifyReleaseAlerts: false,
                notifyProductUpdates: true,
                notifyPromotions: false));

        var handler = new GetProfile.Handler(identity.Object);
        var result = await handler.Handle(
            new GetProfile.Query { UserPublicId = UserPublicId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Owner Person", result.Data!.FullName);
        Assert.Equal(456.78m, result.Data.Balance);
        Assert.True(result.Data.HasPinSet);
        Assert.Equal(createdAt, result.Data.CreatedAt);
        Assert.True(result.Data.Notifications.Login);
        Assert.False(result.Data.Notifications.Release);
        Assert.True(result.Data.Notifications.Updates);
        Assert.False(result.Data.Notifications.Promotions);
    }

    [Fact]
    public async Task HomeQuery_ReturnsOnlyTheRequestingUsersWalletsAndAmounts()
    {
        var category = new WalletCategory
        {
            Name = "Savings",
            Icon = "PiggyBank"
        };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();
        await SeedWalletAsync(category.Id, UserPublicId, "Owner wallet");

        var otherWallet = new Wallet
        {
            UserPublicId = OtherUserPublicId,
            CategoryId = category.Id,
            Name = "Other user's private wallet",
            TargetAmount = Money.FromNaira(10_000m),
            LockedAmount = Money.FromNaira(9_000m),
            AvailableAmount = Money.FromNaira(1_000m),
            FundedAmount = Money.FromNaira(10_000m),
            Status = WalletStatus.Active
        };
        await UnitOfWork.AddAsync(otherWallet);
        await UnitOfWork.SaveChangesAsync();

        var identity = new Mock<IIdentityService>();
        identity
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(UserPublicId));
        var handler = new HomeQuery.Handler(UnitOfWork, identity.Object);

        var result = await handler.Handle(
            new HomeQuery.Query { UserPublicId = UserPublicId },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Single(result.Data!.Wallets);
        Assert.Equal("Owner wallet", result.Data.Wallets[0].WalletName);
        Assert.Equal(60m, result.Data.Balance.TotalLockedAmount);
        Assert.Equal(0m, result.Data.Balance.TotalAvailableAmount);
        Assert.DoesNotContain(
            result.Data.Wallets,
            wallet => wallet.WalletName == "Other user's private wallet");
    }

    [Theory]
    [InlineData("hashed-pin", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public async Task GetIfPinIsSet_ReturnsWhetherTheRequestedUserHasAPin(
        string? pinHash,
        bool expected)
    {
        var identity = new Mock<IIdentityService>();
        identity
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateIdentity(
                UserPublicId,
                transactionPinHash: pinHash));

        var handler = new GetIfPinIsSetQuery.Handler(identity.Object);
        var result = await handler.Handle(
            new GetIfPinIsSetQuery.Query { UserPublicId = UserPublicId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(expected, result.Data!.HasPinSet);
        identity.Verify(
            x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProfile_RejectsMissingOwnerIdentifierWithoutLookingUpAUser()
    {
        var identity = new Mock<IIdentityService>();
        var handler = new GetProfile.Handler(identity.Object);

        var result = await handler.Handle(
            new GetProfile.Query { UserPublicId = " " },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        identity.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private async Task<Wallet> SeedWalletAsync(
        long categoryId,
        string userPublicId,
        string name)
    {
        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            CategoryId = categoryId,
            Name = name,
            TargetAmount = Money.FromNaira(100m),
            LockedAmount = Money.FromNaira(60m),
            FundedAmount = Money.FromNaira(100m),
            Status = WalletStatus.Active
        };

        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();
        return wallet;
    }

    private static UserIdentityDto CreateIdentity(
        string publicId,
        DateTimeOffset? createdAt = null,
        string? transactionPinHash = null,
        bool notifyLoginAlerts = false,
        bool notifyReleaseAlerts = false,
        bool notifyProductUpdates = false,
        bool notifyPromotions = false) =>
        new(
            42,
            publicId,
            "Owner",
            null,
            "Person",
            "owner@example.com",
            "08050000000",
            null,
            Money.FromNaira(456.78m),
            transactionPinHash ?? string.Empty,
            notifyLoginAlerts,
            notifyReleaseAlerts,
            notifyProductUpdates,
            notifyPromotions,
            "device",
            createdAt ?? DateTimeOffset.UtcNow);
}
