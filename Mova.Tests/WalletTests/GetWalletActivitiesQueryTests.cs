using System.Net;
using Microsoft.EntityFrameworkCore;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using GetWalletActivitiesQuery = Mova.Application.BBL.Queries.AccountWallet.GetWalletActivities;

namespace Mova.Tests.Handlers;

public sealed class GetWalletActivitiesQueryTests : BaseTest
{
    private const string UserPublicId = "user_wallet_activities";
    private const string OtherUserPublicId = "someone_else";

    private GetWalletActivitiesQuery.Handler CreateHandler()
    {
        return new GetWalletActivitiesQuery.Handler(UnitOfWork);
    }

    private GetWalletActivitiesQuery.Query CreateQuery(
        long walletId = 1,
        string? userPublicId = null,
        int page = 1,
        int pageSize = 20)
    {
        return new GetWalletActivitiesQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
            WalletId = walletId,
            Page = page,
            PageSize = pageSize,
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
        long? categoryId = null)
    {
        if (categoryId is null || categoryId <= 0)
            throw new InvalidOperationException(
                "Wallet.CategoryId is required and must reference a persisted WalletCategory.");

        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            Name = name,
            CategoryId = categoryId.Value,
            TargetAmount = Money.FromNaira(1000m),
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

    private async Task<Transaction> SeedTransactionAsync(
        long walletId,
        string userPublicId = UserPublicId,
        string title = "Deposit",
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
            CompletedAt = completedAt,
            Reference = $"ref-{Guid.NewGuid():N}",
        };

        await UnitOfWork.AddAsync(transaction);
        await UnitOfWork.SaveChangesAsync();
        return transaction;
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
    public async Task Handle_WithNonPositiveWalletId_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateQuery(walletId: 0),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid wallet ID.", result.Message);
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
    public async Task Handle_WithNoTransactions_ReturnsEmptyGroups()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.TotalActivities);
        Assert.Equal(0, result.Data.TotalCount);
        Assert.Equal(0, result.Data.TotalPages);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTransactionsBelongingToTheWallet()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var otherWallet = await SeedWalletAsync(name: "Other", categoryId: cat.Id);

        await SeedTransactionAsync(wallet.Id, title: "Mine");
        await SeedTransactionAsync(otherWallet.Id, title: "Not mine");

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data!.TotalActivities);

        var activity = Assert.Single(result.Data.Items.SelectMany(g => g.Activities));
        Assert.Equal("Mine", activity.Title);
    }

    [Fact]
    public async Task Handle_ExcludesTransactionsFromOtherUsersOnSameWalletId()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedTransactionAsync(wallet.Id, UserPublicId, title: "Mine");
        await SeedTransactionAsync(wallet.Id, OtherUserPublicId, title: "Not mine");

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        var activity = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal("Mine", activity.Title);
    }

    [Fact]
    public async Task Handle_GroupsTransactionsByDate()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var today = DateTimeOffset.UtcNow;
        var yesterday = today.AddDays(-1);

        await SeedTransactionAsync(wallet.Id, title: "A", completedAt: today);
        await SeedTransactionAsync(wallet.Id, title: "B", completedAt: today.AddMinutes(-5));
        await SeedTransactionAsync(wallet.Id, title: "C", completedAt: yesterday);
        await SeedTransactionAsync(wallet.Id, title: "D", completedAt: yesterday.AddMinutes(-10));
        await SeedTransactionAsync(wallet.Id, title: "E", completedAt: today.AddMinutes(-10));

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data!.Items.Count);
        Assert.Equal(today.Date, result.Data.Items[0].Date);
        Assert.Equal(3, result.Data.Items[0].Activities.Count);
        Assert.Equal(yesterday.Date, result.Data.Items[1].Date);
        Assert.Equal(2, result.Data.Items[1].Activities.Count);
    }

    [Fact]
    public async Task Handle_GroupsAreOrderedNewestFirst()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var today = DateTimeOffset.UtcNow;
        await SeedTransactionAsync(wallet.Id, title: "Old", completedAt: today.AddDays(-3));
        await SeedTransactionAsync(wallet.Id, title: "New", completedAt: today);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.Items[0].Date > result.Data.Items[1].Date);
    }

    [Fact]
    public async Task Handle_ActivitiesWithinGroupAreOrderedNewestFirst()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var today = DateTimeOffset.UtcNow;
        await SeedTransactionAsync(wallet.Id, title: "Older", completedAt: today.AddHours(-3));
        await SeedTransactionAsync(wallet.Id, title: "Newest", completedAt: today);
        await SeedTransactionAsync(wallet.Id, title: "Middle", completedAt: today.AddHours(-1));

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        var group = Assert.Single(result.Data!.Items);
        Assert.Equal(
            new[] { "Newest", "Middle", "Older" },
            group.Activities.Select(a => a.Title).ToArray());
    }

    [Theory]
    [InlineData(TransactionType.Deposit)]
    [InlineData(TransactionType.Release)]
    [InlineData(TransactionType.Refund)]
    public async Task Handle_IsCreditIsTrueForCreditTypes(TransactionType type)
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedTransactionAsync(wallet.Id, title: "Credit", type: type);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        var activity = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.True(activity.IsCredit);
    }

    [Fact]
    public async Task Handle_IsCreditIsFalseForNonCreditTypes()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedTransactionAsync(wallet.Id, title: "Debit", type: TransactionType.Withdrawal);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        var activity = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.False(activity.IsCredit);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectSliceAndMetadata()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var baseTime = DateTimeOffset.UtcNow.AddHours(-10);
        for (var i = 0; i < 7; i++)
        {
            await SeedTransactionAsync(
                wallet.Id,
                title: $"T{i}",
                completedAt: baseTime.AddHours(i));
        }

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id, page: 2, pageSize: 3),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Data!.TotalActivities);
        Assert.Equal(7, result.Data.TotalCount);
        Assert.Equal(3, result.Data.TotalPages);
        Assert.Equal(2, result.Data.Page);
        Assert.Equal(3, result.Data.PageSize);
        Assert.Equal(3, result.Data.Items.SelectMany(g => g.Activities).Count());
    }

    [Fact]
    public async Task Handle_PageSizeOver100_ClampsTo100()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedTransactionAsync(wallet.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id, pageSize: 5_000),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Data!.PageSize);
    }

    [Fact]
    public async Task Handle_InvalidPageAndPageSize_FallBackToDefaults()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedTransactionAsync(wallet.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id, page: 0, pageSize: 0),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(20, result.Data.PageSize);
    }

    [Fact]
    public async Task Handle_DateFallsBackToCreatedAtWhenCompletedAtIsNull()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedTransactionAsync(
            wallet.Id,
            title: "Pending",
            status: TransactionStatus.Pending,
            completedAt: null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        var activity = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.True((DateTimeOffset.UtcNow - activity.Date).TotalMinutes < 5);
    }

    [Fact]
    public async Task Handle_ProjectsAllActivityFields()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var completedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var transaction = await SeedTransactionAsync(
            wallet.Id,
            title: "Rent deposit",
            amountNaira: 250m,
            type: TransactionType.Deposit,
            completedAt: completedAt);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        var activity = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal(transaction.Id, activity.Id);
        Assert.Equal(nameof(TransactionType.Deposit), activity.Type);
        Assert.Equal("Rent deposit", activity.Title);
        Assert.Equal(nameof(TransactionType.Deposit), activity.Subtitle);
        Assert.Equal(250m, activity.Amount);
        Assert.True(activity.IsCredit);
        Assert.Equal(completedAt, activity.Date);
    }
}