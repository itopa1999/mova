using System.Net;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using GetWalletAnalyticsQuery = Mova.Application.BBL.Queries.AccountWallet.GetWalletAnalytics;

namespace Mova.Tests.Handlers;

public sealed class GetWalletAnalyticsQueryTests : BaseTest
{
    private const string UserPublicId = "user_wallet_analytics";
    private const string OtherUserPublicId = "someone_else";

    private GetWalletAnalyticsQuery.Handler CreateHandler()
    {
        return new GetWalletAnalyticsQuery.Handler(UnitOfWork);
    }

    private GetWalletAnalyticsQuery.Query CreateQuery(
        string? userPublicId = null,
        DateTime? date = null)
    {
        return new GetWalletAnalyticsQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
            Date = date,
        };
    }

    private static DateTime FirstOfCurrentMonthUtc()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
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
        decimal fundedNaira = 0m,
        decimal lockedNaira = 0m,
        decimal releasedNaira = 0m,
        decimal withdrawnNaira = 0m,
        decimal availableNaira = 0m,
        decimal targetNaira = 1000m,
        WalletStatus status = WalletStatus.Active,
        PayoutDestination destination = PayoutDestination.Bank)
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
            AvailableAmount = Money.FromNaira(availableNaira),
            LockedAmount = Money.FromNaira(lockedNaira),
            FundedAmount = Money.FromNaira(fundedNaira),
            TotalReleasedAmount = Money.FromNaira(releasedNaira),
            TotalWithdrawnAmount = Money.FromNaira(withdrawnNaira),
            Status = status,
            PayoutDestination = destination,
        };

        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();
        return wallet;
    }

    private async Task SetWalletCreatedAtAsync(Wallet wallet, DateTimeOffset createdAt)
    {
        Context.Entry(wallet).Property("CreatedAt").CurrentValue = createdAt;
        await UnitOfWork.SaveChangesAsync();
    }

    private async Task<Transaction> SeedTransactionAsync(
        long? walletId,
        string userPublicId = UserPublicId,
        string title = "Movement",
        decimal amountNaira = 100m,
        TransactionType type = TransactionType.Deposit,
        TransactionStatus status = TransactionStatus.Completed,
        DateTimeOffset? completedAt = null)
    {
        var transaction = new Transaction
        {
            UserPublicId = userPublicId,
            WalletId = walletId,
            Title = title,
            Amount = Money.FromNaira(amountNaira),
            Type = type,
            Status = status,
            CompletedAt = completedAt ?? DateTimeOffset.UtcNow,
            Reference = $"ref-{Guid.NewGuid():N}",
        };

        await UnitOfWork.AddAsync(transaction);
        await UnitOfWork.SaveChangesAsync();
        return transaction;
    }

    private async Task<RenewalPolicy> SeedRenewalPolicyAsync(
        long walletId,
        string userPublicId = UserPublicId,
        RenewalStatus status = RenewalStatus.Active)
    {
        var policy = new RenewalPolicy
        {
            WalletId = walletId,
            UserPublicId = userPublicId,
            IsEnabled = true,
            Status = status,
        };

        await UnitOfWork.AddAsync(policy);
        await UnitOfWork.SaveChangesAsync();
        return policy;
    }

    [Fact]
    public async Task Handle_WithNoWallets_ReturnsEmptyStateInsight()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal("empty", result.Data!.Insight.Key);
        Assert.Equal(0m, result.Data.MoneyProtected);
        Assert.Equal(0m, result.Data.MoneyReleased);
        Assert.Equal(0m, result.Data.MoneySpent);
        Assert.Equal(0m, result.Data.Remaining);
        Assert.Empty(result.Data.AdditionalInsights);
    }

    [Fact]
    public async Task Handle_WithWalletButNoMovement_ReturnsQuietInsight()
    {
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 0m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("quiet", result.Data!.Insight.Key);
    }

    [Fact]
    public async Task Handle_MoneyProtected_SumsFundedAmountsOfWalletsCreatedThisMonth()
    {
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 400m);
        await SeedWalletAsync(name: "Second", categoryId: cat.Id, fundedNaira: 600m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(1000m, result.Data!.MoneyProtected);
    }

    [Fact]
    public async Task Handle_MoneyProtected_ExcludesWalletsCreatedBeforeTheMonth()
    {
        var cat = await SeedCategoryAsync();
        var oldWallet = await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 500m);
        await SetWalletCreatedAtAsync(oldWallet, DateTimeOffset.UtcNow.AddMonths(-3));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(0m, result.Data!.MoneyProtected);
    }

    [Fact]
    public async Task Handle_MoneyReleased_SumsReleaseTransactionsInMonth()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedTransactionAsync(wallet.Id, amountNaira: 200m, type: TransactionType.Release);
        await SeedTransactionAsync(wallet.Id, amountNaira: 300m, type: TransactionType.Release);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(500m, result.Data!.MoneyReleased);
    }

    [Fact]
    public async Task Handle_MoneyReleased_ExcludesTransactionsOutsideTheMonth()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedTransactionAsync(
            wallet.Id,
            amountNaira: 500m,
            type: TransactionType.Release,
            completedAt: DateTimeOffset.UtcNow.AddDays(-40));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(0m, result.Data!.MoneyReleased);
    }

    [Fact]
    public async Task Handle_MoneyReleased_ExcludesNonCompletedTransactions()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedTransactionAsync(
            wallet.Id,
            amountNaira: 500m,
            type: TransactionType.Release,
            status: TransactionStatus.Pending);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(0m, result.Data!.MoneyReleased);
    }

    [Fact]
    public async Task Handle_MoneySpent_SumsWithdrawalTransactions()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedTransactionAsync(wallet.Id, amountNaira: 150m, type: TransactionType.Withdrawal);
        await SeedTransactionAsync(wallet.Id, amountNaira: 250m, type: TransactionType.Withdrawal);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(400m, result.Data!.MoneySpent);
    }

    [Fact]
    public async Task Handle_Remaining_IsReleasedMinusSpent()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedTransactionAsync(wallet.Id, amountNaira: 1000m, type: TransactionType.Release);
        await SeedTransactionAsync(wallet.Id, amountNaira: 400m, type: TransactionType.Withdrawal);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(600m, result.Data!.Remaining);
    }

    [Fact]
    public async Task Handle_Remaining_NeverGoesNegative()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedTransactionAsync(wallet.Id, amountNaira: 100m, type: TransactionType.Release);
        await SeedTransactionAsync(wallet.Id, amountNaira: 500m, type: TransactionType.Withdrawal);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(0m, result.Data!.Remaining);
    }

    [Fact]
    public async Task Handle_ProtectedPercentage_ComputedAsProtectedOverProtectedPlusReleased()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 750m);

        await SeedTransactionAsync(wallet.Id, amountNaira: 250m, type: TransactionType.Release);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(75m, result.Data!.ProtectedPercentage);
    }

    [Fact]
    public async Task Handle_ProtectedPercentage_IsZeroWhenNoMovement()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedTransactionAsync(wallet.Id, amountNaira: 0m, type: TransactionType.Deposit);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(0m, result.Data!.ProtectedPercentage);
    }

    [Fact]
    public async Task Handle_HighProtectionInsight_FiresWhenProtectedAtLeast80Percent()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 900m);
        await SeedTransactionAsync(wallet.Id, amountNaira: 100m, type: TransactionType.Release);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal("high-protection", result.Data!.Insight.Key);
        Assert.Equal("positive", result.Data.Insight.Tone);
    }

    [Fact]
    public async Task Handle_BalancedInsight_FiresBetweenFiftyAndEightyPercent()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 600m);
        await SeedTransactionAsync(wallet.Id, amountNaira: 400m, type: TransactionType.Release);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal("balanced", result.Data!.Insight.Key);
    }

    [Fact]
    public async Task Handle_SpendHeavyInsight_FiresWhenSpendRateAtLeastSeventyPercent()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 100m);
        await SeedTransactionAsync(wallet.Id, amountNaira: 900m, type: TransactionType.Release);
        await SeedTransactionAsync(wallet.Id, amountNaira: 700m, type: TransactionType.Withdrawal);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal("spend-heavy", result.Data!.Insight.Key);
        Assert.Equal("caution", result.Data.Insight.Tone);
    }

    [Fact]
    public async Task Handle_ReleaseHeavyInsight_FiresWhenNothingElseMatches()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 100m);
        await SeedTransactionAsync(wallet.Id, amountNaira: 900m, type: TransactionType.Release);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal("release-heavy", result.Data!.Insight.Key);
    }

    [Fact]
    public async Task Handle_Month_IsFormattedAsFullMonthAndYear()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var expected = FirstOfCurrentMonthUtc().ToString("MMMM yyyy");
        Assert.Equal(expected, result.Data!.Month);
    }

    [Fact]
    public async Task Handle_ExcludesOtherUsersTransactions()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedTransactionAsync(wallet.Id, amountNaira: 100m, type: TransactionType.Release);
        await SeedTransactionAsync(
            wallet.Id,
            userPublicId: OtherUserPublicId,
            amountNaira: 9999m,
            type: TransactionType.Release);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(100m, result.Data!.MoneyReleased);
    }

    [Fact]
    public async Task Handle_ExcludesOtherUsersWallets()
    {
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 100m);
        await SeedWalletAsync(
            OtherUserPublicId, "Theirs", cat.Id, fundedNaira: 9999m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(100m, result.Data!.MoneyProtected);
    }

    [Fact]
    public async Task Handle_AdditionalInsights_AreLimitedToThree()
    {
        var cat = await SeedCategoryAsync();

        for (var i = 0; i < 4; i++)
        {
            await SeedWalletAsync(
                name: $"Paused{i}",
                categoryId: cat.Id,
                fundedNaira: 100m,
                lockedNaira: 50_000m,
                availableNaira: 1_000m,
                status: WalletStatus.Paused,
                destination: PayoutDestination.Main);
        }

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.Data!.AdditionalInsights.Count <= 3);
    }

    [Fact]
    public async Task Handle_NoAutomationInsight_FiresWhenNoPolicyExists()
    {
        var cat = await SeedCategoryAsync();
        await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 100m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Contains(result.Data!.AdditionalInsights, i => i.Key == "no-automation");
    }

    [Fact]
    public async Task Handle_AutomationPowerUserInsight_FiresWhenMultiplePoliciesAreRunning()
    {
        var cat = await SeedCategoryAsync();

        for (var i = 0; i < 3; i++)
        {
            var w = await SeedWalletAsync(
                name: $"Auto{i}",
                categoryId: cat.Id,
                fundedNaira: 100m);
            await SeedRenewalPolicyAsync(w.Id);
        }

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Contains(result.Data!.AdditionalInsights, i => i.Key == "automation-power-user");
    }

    [Fact]
    public async Task Handle_ManyPausedInsight_FiresWhenThreeOrMorePaused()
    {
        var cat = await SeedCategoryAsync();

        for (var i = 0; i < 3; i++)
        {
            await SeedWalletAsync(
                name: $"Paused{i}",
                categoryId: cat.Id,
                status: WalletStatus.Paused);
        }

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Contains(result.Data!.AdditionalInsights, i => i.Key == "many-paused");
    }

    [Fact]
    public async Task Handle_WithDateParameter_UsesSelectedMonth()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 500m);

        var past = FirstOfCurrentMonthUtc().AddMonths(-2);
        await SetWalletCreatedAtAsync(wallet, new DateTimeOffset(past, TimeSpan.Zero));

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(date: past),
            default);

        Assert.Equal(past.ToString("MMMM yyyy"), result.Data!.Month);
        Assert.Equal(500m, result.Data.MoneyProtected);
    }

    [Fact]
    public async Task Handle_ExcludesWalletsCreatedAfterTheMonth()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, fundedNaira: 500m);

        var lastMonth = FirstOfCurrentMonthUtc().AddMonths(-1);
        var futureCreatedAt = FirstOfCurrentMonthUtc().AddDays(1);

        await SetWalletCreatedAtAsync(wallet, new DateTimeOffset(futureCreatedAt, TimeSpan.Zero));

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(date: lastMonth),
            default);

        Assert.Equal(0m, result.Data!.MoneyProtected);
        Assert.Equal("empty", result.Data.Insight.Key);
    }
}