using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using GetRenewalPolicyQuery = Mova.Application.BBL.Queries.AccountWallet.GetRenewalPolicyQuery;

namespace Mova.Tests.Handlers;

public sealed class GetRenewalPolicyQueryTests : BaseTest
{
    private const string UserPublicId = "user_renewal_policy";
    private const string OtherUserPublicId = "someone_else";

    private GetRenewalPolicyQuery.Handler CreateHandler()
    {
        return new GetRenewalPolicyQuery.Handler(
            UnitOfWork);
    }

    private GetRenewalPolicyQuery.Query CreateQuery(
        long walletId = 1,
        string? userPublicId = null)
    {
        return new GetRenewalPolicyQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
            WalletId = walletId,
        };
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
        decimal targetNaira = 1000m)
    {
        if (categoryId is null || categoryId <= 0)
            throw new InvalidOperationException(
                "Wallet.CategoryId is required and must reference a persisted WalletCategory.");

        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            Name = name,
            CategoryId = categoryId.Value,
            TargetAmount = Money.FromNaira(targetNaira),
            AvailableAmount = Money.FromNaira(0),
            LockedAmount = Money.FromNaira(0),
            FundedAmount = Money.FromNaira(0),
            TotalReleasedAmount = Money.FromNaira(0),
            TotalWithdrawnAmount = Money.FromNaira(0),
            Status = WalletStatus.Active,
        };

        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();
        return wallet;
    }

    private async Task<RenewalPolicy> SeedRenewalPolicyAsync(
        long walletId,
        string userPublicId = UserPublicId,
        bool isEnabled = true,
        RenewalStatus status = RenewalStatus.Active,
        RenewalTriggerType triggerType = RenewalTriggerType.OnCompletion,
        decimal triggerNaira = 100m,
        RefillAmountType refillAmountType = RefillAmountType.Fixed,
        decimal refillNaira = 500m,
        decimal minMainBalanceNaira = 0m,
        bool refillUntilMainBalanceExhausted = false,
        int? maxRenewals = null,
        int renewalsCount = 0)
    {
        var policy = new RenewalPolicy
        {
            WalletId = walletId,
            UserPublicId = userPublicId,
            IsEnabled = isEnabled,
            Status = status,
            TriggerType = triggerType,
            TriggerAmount = Money.FromNaira(triggerNaira),
            RefillAmountType = refillAmountType,
            RefillAmount = Money.FromNaira(refillNaira),
            MinMainBalance = Money.FromNaira(minMainBalanceNaira),
            RefillUntilMainBalanceExhausted = refillUntilMainBalanceExhausted,
            MaxRenewals = maxRenewals,
            RenewalsCount = renewalsCount,
        };

        await UnitOfWork.AddAsync(policy);
        await UnitOfWork.SaveChangesAsync();
        return policy;
    }

    [Fact]
    public async Task Handle_WithUnknownWallet_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateQuery(walletId: 999_999),
            default);

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
        var result = await handler.Handle(
            CreateQuery(walletId: otherWallet.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Wallet not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithWalletOwnedByOtherUser_DoesNotLeakPolicyData()
    {
        var cat = await SeedCategoryAsync();
        var otherWallet = await SeedWalletAsync(OtherUserPublicId, "Theirs", cat.Id);
        await SeedRenewalPolicyAsync(otherWallet.Id, OtherUserPublicId);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: otherWallet.Id),
            default);

        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Handle_WithNoPolicy_ReturnsNotFound()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("This wallet has no automation policy.", result.Message);
    }

    [Fact]
    public async Task Handle_WithPolicy_ReturnsWalletAndPolicyIds()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(name: "Rent fund", categoryId: cat.Id);
        var policy = await SeedRenewalPolicyAsync(wallet.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(policy.Id, result.Data!.RenewalPolicyId);
        Assert.Equal(wallet.Id, result.Data.WalletId);
        Assert.Equal("Rent fund", result.Data.WalletName);
    }

    [Fact]
    public async Task Handle_WithFixedRefillType_ReturnsTargetAmountAsEffective()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, targetNaira: 2_500m);
        await SeedRenewalPolicyAsync(
            wallet.Id,
            refillAmountType: RefillAmountType.Fixed,
            refillNaira: 999m);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(RefillAmountType.Fixed), result.Data!.RefillAmountType);
        Assert.Equal(999m, result.Data.RefillAmount);
        Assert.Equal(2_500m, result.Data.RefillAmountEffective);
    }

    [Fact]
    public async Task Handle_WithNonFixedRefillType_ReturnsRefillAmountAsEffective()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, targetNaira: 2_500m);
        await SeedRenewalPolicyAsync(
            wallet.Id,
            refillAmountType: RefillAmountType.Custom,
            refillNaira: 750m);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(nameof(RefillAmountType.Custom), result.Data!.RefillAmountType);
        Assert.Equal(750m, result.Data.RefillAmount);
        Assert.Equal(750m, result.Data.RefillAmountEffective);
    }

    [Fact]
    public async Task Handle_WithNullMaxRenewals_ReturnsNullRenewalsRemaining()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedRenewalPolicyAsync(
            wallet.Id,
            maxRenewals: null,
            renewalsCount: 7);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data!.MaxRenewals);
        Assert.Equal(7, result.Data.RenewalsCount);
        Assert.Null(result.Data.RenewalsRemaining);
    }

    [Fact]
    public async Task Handle_WithMaxRenewals_ComputesRemainingCorrectly()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedRenewalPolicyAsync(
            wallet.Id,
            maxRenewals: 10,
            renewalsCount: 4);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Data!.MaxRenewals);
        Assert.Equal(4, result.Data.RenewalsCount);
        Assert.Equal(6, result.Data.RenewalsRemaining);
    }

    [Fact]
    public async Task Handle_WithRenewalsCountGreaterThanMax_ClampsRemainingToZero()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedRenewalPolicyAsync(
            wallet.Id,
            maxRenewals: 3,
            renewalsCount: 8);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Data!.RenewalsRemaining);
    }

    [Fact]
    public async Task Handle_WithDisabledPolicy_ReturnsIsEnabledFalse()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedRenewalPolicyAsync(
            wallet.Id,
            isEnabled: false,
            status: RenewalStatus.Paused);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Data!.IsEnabled);
        Assert.Equal(nameof(RenewalStatus.Paused), result.Data.Status);
    }

    [Fact]
    public async Task Handle_ProjectsAllPolicyFields()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedRenewalPolicyAsync(
            wallet.Id,
            isEnabled: true,
            status: RenewalStatus.Active,
            triggerType: RenewalTriggerType.OnCompletion,
            triggerNaira: 150m,
            refillAmountType: RefillAmountType.Fixed,
            refillNaira: 400m,
            minMainBalanceNaira: 25m,
            refillUntilMainBalanceExhausted: true,
            maxRenewals: 12,
            renewalsCount: 2);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        var dto = result.Data!;
        Assert.True(dto.IsEnabled);
        Assert.Equal(nameof(RenewalStatus.Active), dto.Status);
        Assert.Equal(nameof(RenewalTriggerType.OnCompletion), dto.TriggerType);
        Assert.Equal(150m, dto.TriggerAmount);
        Assert.Equal(nameof(RefillAmountType.Fixed), dto.RefillAmountType);
        Assert.Equal(400m, dto.RefillAmount);
        Assert.Equal(25m, dto.MinMainBalance);
        Assert.True(dto.RefillUntilMainBalanceExhausted);
        Assert.Equal(12, dto.MaxRenewals);
        Assert.Equal(2, dto.RenewalsCount);
        Assert.Equal(10, dto.RenewalsRemaining);
        Assert.NotEqual(default, dto.CreatedAt);
    }

    [Fact]
    public async Task Handle_OnlyLooksUpPolicyForTheRequestedWallet()
    {
        var cat = await SeedCategoryAsync();

        var mine = await SeedWalletAsync(UserPublicId, "Mine", cat.Id);
        var another = await SeedWalletAsync(UserPublicId, "Another", cat.Id);

        var myPolicy = await SeedRenewalPolicyAsync(mine.Id, refillNaira: 111m);
        await SeedRenewalPolicyAsync(another.Id, refillNaira: 999m);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: mine.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(myPolicy.Id, result.Data!.RenewalPolicyId);
        Assert.Equal(111m, result.Data.RefillAmount);
    }
}