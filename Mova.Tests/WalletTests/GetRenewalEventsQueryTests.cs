using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using GetRenewalEventsQuery = Mova.Application.BBL.Queries.AccountWallet.GetRenewalEventsQuery;

namespace Mova.Tests.Handlers;

public sealed class GetRenewalEventsQueryTests : BaseTest
{
    private const string UserPublicId = "user_renewal_events";
    private const string OtherUserPublicId = "someone_else";

    private GetRenewalEventsQuery.Handler CreateHandler()
    {
        return new GetRenewalEventsQuery.Handler(
            UnitOfWork,
            NullLogger<GetRenewalEventsQuery.Handler>.Instance);
    }

    private GetRenewalEventsQuery.Query CreateQuery(
        long walletId = 1,
        string? userPublicId = null,
        int page = 1,
        int pageSize = 20)
    {
        return new GetRenewalEventsQuery.Query
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

    private async Task<RenewalPolicy> GetOrCreateRenewalPolicyAsync(
        long walletId,
        string userPublicId = UserPublicId)
    {
        var existing = await UnitOfWork.Query<RenewalPolicy>()
            .FirstOrDefaultAsync(p => p.WalletId == walletId);

        if (existing is not null)
            return existing;

        var policy = new RenewalPolicy
        {
            WalletId = walletId,
            UserPublicId = userPublicId,
        };

        await UnitOfWork.AddAsync(policy);
        await UnitOfWork.SaveChangesAsync();
        return policy;
    }

    private async Task<Transaction> SeedTransactionAsync(
        string userPublicId = UserPublicId,
        string title = "Renewal refill",
        decimal amountNaira = 250m,
        TransactionType type = TransactionType.Deposit,
        long? walletId = null)
    {
        var transaction = new Transaction
        {
            UserPublicId = userPublicId,
            WalletId = walletId,
            Title = title,
            Amount = Money.FromNaira(amountNaira),
            Type = type,
            Status = TransactionStatus.Completed,
            Reference = $"ref-{Guid.NewGuid():N}",
        };

        await UnitOfWork.AddAsync(transaction);
        await UnitOfWork.SaveChangesAsync();
        return transaction;
    }

    private async Task<RenewalEvent> SeedRenewalEventAsync(
        long walletId,
        DateTimeOffset occurredAt,
        decimal amountNaira = 50m,
        string? reason = null,
        long? transactionId = null,
        string userPublicId = UserPublicId)
    {
        var policy = await GetOrCreateRenewalPolicyAsync(walletId, userPublicId);

        var renewalEvent = new RenewalEvent
        {
            WalletId = walletId,
            RenewalPolicyId = policy.Id,
            UserPublicId = userPublicId,
            OccurredAt = occurredAt,
            Result = RenewalResult.Succeeded,
            Reason = reason,
            Amount = Money.FromNaira(amountNaira),
            TransactionId = transactionId,
        };

        await UnitOfWork.AddAsync(renewalEvent);
        await UnitOfWork.SaveChangesAsync();
        return renewalEvent;
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
    public async Task Handle_WithWalletOwnedByOtherUser_DoesNotLeakWalletName()
    {
        var cat = await SeedCategoryAsync();
        var otherWallet = await SeedWalletAsync(OtherUserPublicId, "Secret wallet", cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: otherWallet.Id),
            default);

        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Handle_WithNoEvents_ReturnsEmptyPageWithZeroTotals()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.TotalCount);
        Assert.Equal(0, result.Data.TotalPages);
    }

    [Fact]
    public async Task Handle_ReturnsWalletIdAndNameInResponse()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(name: "Rent fund", categoryId: cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(wallet.Id, result.Data!.WalletId);
        Assert.Equal("Rent fund", result.Data.WalletName);
    }

    [Fact]
    public async Task Handle_ReturnsEventsForTheRequestedWallet()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedRenewalEventAsync(wallet.Id, DateTimeOffset.UtcNow.AddMinutes(-3), amountNaira: 100m);
        await SeedRenewalEventAsync(wallet.Id, DateTimeOffset.UtcNow.AddMinutes(-2), amountNaira: 200m);
        await SeedRenewalEventAsync(wallet.Id, DateTimeOffset.UtcNow.AddMinutes(-1), amountNaira: 300m);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Data!.TotalCount);
        Assert.Equal(3, result.Data.Items.Count);
    }

    [Fact]
    public async Task Handle_ExcludesEventsFromOtherWallets()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var otherWallet = await SeedWalletAsync(name: "Other wallet", categoryId: cat.Id);

        await SeedRenewalEventAsync(wallet.Id, DateTimeOffset.UtcNow, amountNaira: 100m);
        await SeedRenewalEventAsync(otherWallet.Id, DateTimeOffset.UtcNow, amountNaira: 999m);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(100m, item.Amount);
    }

    [Fact]
    public async Task Handle_OrdersByOccurredAtDescendingThenIdDescending()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var t1 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t3 = DateTimeOffset.UtcNow.AddMinutes(-1);

        await SeedRenewalEventAsync(wallet.Id, t1, amountNaira: 100m);
        await SeedRenewalEventAsync(wallet.Id, t3, amountNaira: 300m);
        await SeedRenewalEventAsync(wallet.Id, t2, amountNaira: 200m);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        var amounts = result.Data!.Items.Select(i => i.Amount).ToList();
        Assert.Equal(new[] { 300m, 200m, 100m }, amounts);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectSliceAndMetadata()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-20);
        for (var i = 0; i < 7; i++)
        {
            await SeedRenewalEventAsync(
                wallet.Id,
                baseTime.AddMinutes(i),
                amountNaira: 100m + i);
        }

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id, page: 2, pageSize: 3),
            default);

        Assert.True(result.IsSuccess);
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
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedRenewalEventAsync(wallet.Id, DateTimeOffset.UtcNow);

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
        await SeedRenewalEventAsync(wallet.Id, DateTimeOffset.UtcNow);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id, page: 0, pageSize: 0),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(20, result.Data.PageSize);
    }

    [Fact]
    public async Task Handle_ProjectsAllEventFields()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var transaction = await SeedTransactionAsync(
            title: "Renewal refill",
            amountNaira: 250m,
            walletId: wallet.Id);

        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await SeedRenewalEventAsync(
            wallet.Id,
            occurredAt,
            amountNaira: 250m,
            reason: "Insufficient balance",
            transactionId: transaction.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(occurredAt, item.OccurredAt);
        Assert.Equal(nameof(RenewalResult.Succeeded), item.Result);
        Assert.Equal("Insufficient balance", item.Reason);
        Assert.Equal(250m, item.Amount);
        Assert.Equal(transaction.Id, item.TransactionId);
    }
}