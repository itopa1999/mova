using System.Net;
using Microsoft.EntityFrameworkCore;
using Moq;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class HomeQueryTests : BaseTest
{
    private const string UserPublicId = "user_home_test";
    private const string OtherUserPublicId = "someone_else";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";

    private readonly Mock<IIdentityService> _identityService = new();

    private HomeQuery.Handler CreateHandler()
    {
        return new HomeQuery.Handler(
            UnitOfWork,
            _identityService.Object);
    }

    private HomeQuery.Query CreateQuery(string? userPublicId = null)
    {
        return new HomeQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
        };
    }

    // ---------------------------------------------------------
    // Mock setups
    // ---------------------------------------------------------

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
                null,                    // OtherNames
                "Starboy",               // LastName
                email,
                "08050000000",           // PhoneNumber
                null,                    // ProfilePicture
                Money.FromNaira(balanceNaira),
                string.Empty,            // TransactionPinHash
                true,                    // NotifyLoginAlerts
                true,                    // NotifyReleaseAlerts
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

    // ── Permissions mock helpers ─────────────────────────────

    private static UserPermissionsDto BuildPermissions(
        UserAccountStatus status = UserAccountStatus.Active,
        string statusLabel = "Active",
        string statusDescription = "Your account is in good standing and fully operational.",
        bool canLogin = true,
        bool canCreateWallets = true,
        bool canPerformSensitiveOperations = true,
        bool canReceivePayouts = true,
        bool canTopUp = true,
        string? restrictionReason = null,
        string? restrictionReasonDetails = null,
        DateTimeOffset? restrictionExpiresAt = null)
        => new(
            UserPublicId,
            status,
            statusLabel,
            statusDescription,
            canLogin,
            canCreateWallets,
            canPerformSensitiveOperations,
            canReceivePayouts,
            canTopUp,
            restrictionReason,
            restrictionReasonDetails,
            null,
            restrictionExpiresAt);

    private void SetupPermissions(UserPermissionsDto? permissions = null)
    {
        _identityService
            .Setup(x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions ?? BuildPermissions());
    }

    private void SetupPermissionsReturnsNull()
    {
        _identityService
            .Setup(x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPermissionsDto?)null);
    }

    // ---------------------------------------------------------
    // Seeds
    // ---------------------------------------------------------

    private async Task<WalletCategory> SeedCategoryAsync(
        string name = "Savings",
        string icon = "PiggyBank")
    {
        var category = new WalletCategory
        {
            Name = name,
            Icon = icon,
        };

        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();
        return category;
    }

    private async Task<Wallet> SeedWalletAsync(
        string userPublicId = UserPublicId,
        string name = "Rent",
        long? categoryId = null,
        decimal targetNaira = 100_000m,
        decimal availableNaira = 0m,
        decimal lockedNaira = 0m,
        WalletStatus status = WalletStatus.Active)
    {
        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            Name = name,
            CategoryId = categoryId ?? 0,
            TargetAmount = Money.FromNaira(targetNaira),
            AvailableAmount = Money.FromNaira(availableNaira),
            LockedAmount = Money.FromNaira(lockedNaira),
            FundedAmount = Money.FromNaira(0),
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
        decimal amountNaira = 5000m,
        ReleaseFrequency frequency = ReleaseFrequency.Monthly,
        string frequencyConfig = "{}",
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null)
    {
        var start = startDate ?? DateTimeOffset.UtcNow;

        var rule = new WalletRule
        {
            WalletId = walletId,
            Amount = Money.FromNaira(amountNaira),
            Frequency = frequency,
            FrequencyConfig = frequencyConfig,
            StartDate = start,
            EndDate = endDate ?? start.AddYears(1),
        };

        await UnitOfWork.AddAsync(rule);
        await UnitOfWork.SaveChangesAsync();
        return rule;
    }

    private async Task<ScheduledRelease> SeedReleaseAsync(
        long walletId,
        long walletRuleId,
        decimal amountNaira,
        DateTimeOffset releasedAt,
        ReleaseStatus status = ReleaseStatus.Released)
    {
        var release = new ScheduledRelease
        {
            WalletId = walletId,
            WalletRuleId = walletRuleId,
            Amount = Money.FromNaira(amountNaira),
            ScheduledFor = releasedAt,
            ReleasedAt = status == ReleaseStatus.Released ? releasedAt : null,
            Status = status,
            FailedAttempts = 0,
        };

        await UnitOfWork.AddAsync(release);
        await UnitOfWork.SaveChangesAsync();
        return release;
    }

    // =========================================================
    // 1. Validation
    // =========================================================

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
    public async Task Handle_WithEmptyUserPublicId_DoesNotCheckPermissions()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateQuery(userPublicId: ""), default);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================
    // 2. User lookup
    // =========================================================

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
    public async Task Handle_WithUnknownUser_DoesNotCheckPermissions()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(), default);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================
    // 3. Empty state
    // =========================================================

    [Fact]
    public async Task Handle_WithNoWallets_ReturnsEmptyCollections()
    {
        SetupUserExists(balanceNaira: 1000m);
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.Wallets);
        Assert.Empty(result.Data.TodayReleased);
        Assert.Equal(0m, result.Data.Balance.TotalAvailableAmount);
        Assert.Equal(0m, result.Data.Balance.TotalLockedAmount);
        Assert.Equal(1000m, result.Data.Balance.UserBalance);
    }

    [Fact]
    public async Task Handle_WithNoWallets_ReturnsFiveLockedAmountPoints()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(5, result.Data!.LockedAmountHistory.Count);
        Assert.All(result.Data.LockedAmountHistory, p => Assert.Equal(0m, p.Value));
    }

    // =========================================================
    // 4. Balance aggregation
    // =========================================================

    [Fact]
    public async Task Handle_SumsAvailableAndLockedAcrossActiveWallets()
    {
        SetupUserExists(balanceNaira: 500m);
        SetupPermissions();
        var category = await SeedCategoryAsync();
        await SeedWalletAsync(name: "W1", categoryId: category.Id, availableNaira: 100m, lockedNaira: 200m);
        await SeedWalletAsync(name: "W2", categoryId: category.Id, availableNaira: 50m, lockedNaira: 300m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(150m, result.Data!.Balance.TotalAvailableAmount);
        Assert.Equal(500m, result.Data.Balance.TotalLockedAmount);
        Assert.Equal(500m, result.Data.Balance.UserBalance);
    }

    [Fact]
public async Task Handle_SumsAllUserWalletsForBalance()
{
    SetupUserExists();
    SetupPermissions();
    var category = await SeedCategoryAsync();
    await SeedWalletAsync(name: "Active", categoryId: category.Id, availableNaira: 100m, lockedNaira: 200m, status: WalletStatus.Active);
    await SeedWalletAsync(name: "Paused", categoryId: category.Id, availableNaira: 999m, lockedNaira: 999m, status: WalletStatus.Paused);
    await SeedWalletAsync(name: "Broken", categoryId: category.Id, availableNaira: 999m, lockedNaira: 999m, status: WalletStatus.Broken);
    await SeedWalletAsync(name: "Closed", categoryId: category.Id, availableNaira: 999m, lockedNaira: 999m, status: WalletStatus.Closed);

    var handler = CreateHandler();
    var result = await handler.Handle(CreateQuery(), default);

    // Available: 100 + 999 + 999 + 999 = 3097
    Assert.Equal(3097m, result.Data!.Balance.TotalAvailableAmount);

    // Locked: 200 + 999 + 999 + 999 = 3197
    Assert.Equal(3197m, result.Data.Balance.TotalLockedAmount);
}

    // =========================================================
    // 5. Wallet list
    // =========================================================

    [Fact]
public async Task Handle_ReturnsAtMostEightWallets()
{
    SetupUserExists();
    SetupPermissions();
    var category = await SeedCategoryAsync();
    for (var i = 0; i < 10; i++)
    {
        await SeedWalletAsync(name: $"Wallet {i}", categoryId: category.Id);
    }

    var handler = CreateHandler();
    var result = await handler.Handle(CreateQuery(), default);

    Assert.Equal(8, result.Data!.Wallets.Count);
}

    [Fact]
public async Task Handle_WalletList_ReturnsNewestEight()
{
    SetupUserExists();
    SetupPermissions();
    var category = await SeedCategoryAsync();
    for (var i = 0; i < 10; i++)
    {
        await SeedWalletAsync(name: $"Wallet {i}", categoryId: category.Id);
    }

    var handler = CreateHandler();
    var result = await handler.Handle(CreateQuery(), default);

    // All 10 wallets are Active → ordered by CreatedAt DESC → top 8
    Assert.Equal("Wallet 9", result.Data!.Wallets[0].WalletName);
    Assert.Equal("Wallet 2", result.Data.Wallets[7].WalletName);
}

    [Fact]
    public async Task Handle_WalletList_OnlyIncludesRequestingUsersWallets()
    {
        SetupUserExists();
        SetupPermissions();
        var category = await SeedCategoryAsync();
        await SeedWalletAsync(userPublicId: UserPublicId, name: "Mine", categoryId: category.Id);
        await SeedWalletAsync(userPublicId: OtherUserPublicId, name: "Not mine", categoryId: category.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Single(result.Data!.Wallets);
        Assert.Equal("Mine", result.Data.Wallets[0].WalletName);
    }

    [Fact]
public async Task Handle_WalletList_IncludesAllStatusesOrderedByStatusPriority()
{
    SetupUserExists();
    SetupPermissions();
    var category = await SeedCategoryAsync();
    await SeedWalletAsync(name: "Paused", categoryId: category.Id, status: WalletStatus.Paused);
    await SeedWalletAsync(name: "Active", categoryId: category.Id, status: WalletStatus.Active);
    await SeedWalletAsync(name: "Closed", categoryId: category.Id, status: WalletStatus.Closed);

    var handler = CreateHandler();
    var result = await handler.Handle(CreateQuery(), default);

    Assert.Equal(3, result.Data!.Wallets.Count);

    // Active first, Paused second, Closed last
    Assert.Equal("Active", result.Data.Wallets[0].WalletName);
    Assert.Equal("Paused", result.Data.Wallets[1].WalletName);
    Assert.Equal("Closed", result.Data.Wallets[2].WalletName);
}

    [Fact]
    public async Task Handle_WalletList_IncludesCategoryDetails()
    {
        SetupUserExists();
        SetupPermissions();
        var category = await SeedCategoryAsync(name: "Rent", icon: "Home");
        await SeedWalletAsync(name: "Rent Fund", categoryId: category.Id, targetNaira: 50_000m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var dto = Assert.Single(result.Data!.Wallets);
        Assert.Equal("Rent Fund", dto.WalletName);
        Assert.Equal(category.Id, dto.CategoryId);
        Assert.Equal("Rent", dto.CategoryName);
        Assert.Equal("Home", dto.CategoryIcon);
        Assert.Equal(50_000m, dto.TargetAmount);
    }

    // =========================================================
    // 6. Today released
    // =========================================================

    [Fact]
    public async Task Handle_IncludesReleasesFromToday()
    {
        SetupUserExists();
        SetupPermissions();
        var category = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(name: "Rent", categoryId: category.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(
            wallet.Id,
            rule.Id,
            amountNaira: 5000m,
            releasedAt: DateTimeOffset.UtcNow);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var dto = Assert.Single(result.Data!.TodayReleased);
        Assert.Equal("Rent", dto.WalletName);
        Assert.Equal(5000m, dto.ReleasedAmount);
    }

    [Fact]
    public async Task Handle_ExcludesReleasesFromYesterday()
    {
        SetupUserExists();
        SetupPermissions();
        var category = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(name: "Rent", categoryId: category.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(
            wallet.Id,
            rule.Id,
            amountNaira: 5000m,
            releasedAt: DateTimeOffset.UtcNow.AddDays(-1));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Empty(result.Data!.TodayReleased);
    }

    [Fact]
    public async Task Handle_ExcludesPendingReleases()
    {
        SetupUserExists();
        SetupPermissions();
        var category = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(name: "Rent", categoryId: category.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(
            wallet.Id,
            rule.Id,
            amountNaira: 5000m,
            releasedAt: DateTimeOffset.UtcNow,
            status: ReleaseStatus.Paused);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Empty(result.Data!.TodayReleased);
    }

    [Fact]
    public async Task Handle_ReturnsAtMostFiveReleasesToday()
    {
        SetupUserExists();
        SetupPermissions();
        var category = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(name: "Rent", categoryId: category.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        for (var i = 0; i < 8; i++)
        {
            await SeedReleaseAsync(
                wallet.Id,
                rule.Id,
                amountNaira: 1000m + i,
                releasedAt: DateTimeOffset.UtcNow.AddMinutes(-i));
        }

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(5, result.Data!.TodayReleased.Count);
    }

    [Fact]
    public async Task Handle_DoesNotIncludeOtherUsersReleases()
    {
        SetupUserExists();
        SetupPermissions();
        var category = await SeedCategoryAsync();
        var myWallet = await SeedWalletAsync(userPublicId: UserPublicId, categoryId: category.Id);
        var otherWallet = await SeedWalletAsync(userPublicId: OtherUserPublicId, categoryId: category.Id);
        var myRule = await SeedWalletRuleAsync(myWallet.Id);
        var otherRule = await SeedWalletRuleAsync(otherWallet.Id);

        await SeedReleaseAsync(myWallet.Id, myRule.Id, 100m, DateTimeOffset.UtcNow);
        await SeedReleaseAsync(otherWallet.Id, otherRule.Id, 999m, DateTimeOffset.UtcNow);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var dto = Assert.Single(result.Data!.TodayReleased);
        Assert.Equal(100m, dto.ReleasedAmount);
    }

    // =========================================================
    // 7. Locked amount history
    // =========================================================

    [Fact]
    public async Task Handle_LockedAmountHistory_HasFiveMonthsInOrder()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var labels = result.Data!.LockedAmountHistory.Select(p => p.Label).ToList();
        Assert.Equal(5, labels.Count);

        var parsed = labels
            .Select(l => DateTime.ParseExact(
                l, "MMM yyyy", System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        for (var i = 1; i < parsed.Count; i++)
        {
            Assert.True(parsed[i] > parsed[i - 1],
                $"Label {labels[i]} should be after {labels[i - 1]}");
        }
    }

    [Fact]
    public async Task Handle_LockedAmountHistory_SumsTargetAmountPerMonth()
    {
        SetupUserExists();
        SetupPermissions();
        var category = await SeedCategoryAsync();

        await SeedWalletAsync(name: "W1", categoryId: category.Id, targetNaira: 1000m);
        await SeedWalletAsync(name: "W2", categoryId: category.Id, targetNaira: 2500m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var currentMonthPoint = result.Data!.LockedAmountHistory.Last();
        Assert.Equal(3500m, currentMonthPoint.Value);
    }

    [Fact]
    public async Task Handle_LockedAmountHistory_ExcludesOtherUsersWallets()
    {
        SetupUserExists();
        SetupPermissions();
        var category = await SeedCategoryAsync();

        await SeedWalletAsync(userPublicId: UserPublicId, categoryId: category.Id, targetNaira: 1000m);
        await SeedWalletAsync(userPublicId: OtherUserPublicId, categoryId: category.Id, targetNaira: 9999m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var currentMonthPoint = result.Data!.LockedAmountHistory.Last();
        Assert.Equal(1000m, currentMonthPoint.Value);
    }

    // =========================================================
    // 8. Account status
    // =========================================================

    [Fact]
    public async Task Handle_AlwaysIncludesAccountStatus()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.AccountStatus);
    }

    [Fact]
    public async Task Handle_WithActiveAccount_StatusIsHealthy()
    {
        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Active,
            statusLabel: "Active",
            statusDescription: "Your account is in good standing and fully operational.",
            restrictionExpiresAt: null));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Active", status.Status);
        Assert.Equal("Active", status.StatusLabel);
        Assert.True(status.IsHealthy);
        Assert.Null(status.RestrictionReason);
        Assert.Null(status.RestrictionReasonDetails);
        Assert.Null(status.RestrictionExpiresAt);
    }

    [Fact]
    public async Task Handle_WithRestrictedAccount_IsNotHealthy()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Restricted,
            statusLabel: "Restricted",
            statusDescription: "Some features are temporarily limited.",
            canCreateWallets: false,
            canPerformSensitiveOperations: false,
            restrictionReason: "TermsViolation",
            restrictionReasonDetails: "Three warnings in 30 days.",
            restrictionExpiresAt: expiresAt));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Restricted", status.Status);
        Assert.Equal("Restricted", status.StatusLabel);
        Assert.False(status.IsHealthy);
        Assert.Equal("TermsViolation", status.RestrictionReason);
        Assert.Equal("Three warnings in 30 days.", status.RestrictionReasonDetails);
        Assert.Equal(expiresAt, status.RestrictionExpiresAt);
    }

    [Fact]
    public async Task Handle_WithSuspendedAccount_IsNotHealthy()
    {
        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Suspended,
            statusLabel: "Suspended",
            statusDescription: "Your account is suspended.",
            canLogin: false,
            canCreateWallets: false,
            canPerformSensitiveOperations: false,
            canReceivePayouts: false,
            restrictionReason: "SuspectedFraud",
            restrictionReasonDetails: "Chargebacks from 4 cards."));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Suspended", status.Status);
        Assert.False(status.IsHealthy);
        Assert.Equal("SuspectedFraud", status.RestrictionReason);
    }

    [Fact]
    public async Task Handle_WithClosedAccount_IsNotHealthy()
    {
        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Closed,
            statusLabel: "Closed",
            statusDescription: "Your account is permanently closed.",
            canLogin: false,
            canCreateWallets: false,
            canPerformSensitiveOperations: false,
            canReceivePayouts: false,
            restrictionReason: "UserRequestedDeactivation"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Closed", status.Status);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WithDeactivatedAccount_IsNotHealthy()
    {
        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Deactivated,
            statusLabel: "Deactivated",
            statusDescription: "Your account is deactivated.",
            canLogin: false,
            canCreateWallets: false,
            canPerformSensitiveOperations: false));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Deactivated", status.Status);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WithActiveStatusButExpiredRestriction_IsNotHealthy()
    {
        // Edge case: status says Active but restriction expiry is in the past.
        // The FE should still see the banner, so IsHealthy must be false.
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Active,
            statusLabel: "Active",
            statusDescription: "Your account is in good standing.",
            restrictionExpiresAt: expiredAt));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Active", status.Status);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_ReturnsUnavailableStatus()
    {
        SetupUserExists();
        SetupPermissionsReturnsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data?.AccountStatus);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Unknown", status.Status);
        Assert.Equal("Unavailable", status.StatusLabel);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_StillReturnsHomeData()
    {
        // Fail-safe: the FE still needs wallets, balance, etc. even if the
        // account status couldn't be resolved.
        SetupUserExists(balanceNaira: 500m);
        SetupPermissionsReturnsNull();
        var category = await SeedCategoryAsync();
        await SeedWalletAsync(name: "Rent", categoryId: category.Id, availableNaira: 100m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Data!.Wallets);
        Assert.Equal(100m, result.Data.Balance.TotalAvailableAmount);
        Assert.False(result.Data.AccountStatus.IsHealthy);
    }

    [Fact]
    public async Task Handle_ChecksPermissionsExactlyOnce()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(), default);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================
    // 9. Error path
    // =========================================================

    [Fact]
public async Task Handle_WhenQueryThrows_ReturnsInternalServerError()
{
    SetupUserExists();
    SetupPermissions();

    // Use a throwing unit of work to simulate a DB failure
    var throwingUow = new Mock<IUnitOfWork>();
    throwingUow
        .Setup(x => x.Query<Wallet>())
        .Throws(new InvalidOperationException("Simulated DB failure"));

    var handler = new HomeQuery.Handler(
        throwingUow.Object,
        _identityService.Object);

    var result = await handler.Handle(CreateQuery(), default);

    Assert.False(result.IsSuccess);
    Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
    Assert.Equal(
        "An error occurred while retrieving your home data. Please try again later.",
        result.Message);
}
}