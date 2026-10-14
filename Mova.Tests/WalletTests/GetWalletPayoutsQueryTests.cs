using System.Net;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using GetWalletPayoutsQuery = Mova.Application.BBL.Queries.AccountWallet.GetWalletPayouts;

namespace Mova.Tests.Handlers;

public sealed class GetWalletPayoutsQueryTests : BaseTest
{
    private const string UserPublicId = "user_wallet_payouts";
    private const string OtherUserPublicId = "someone_else";

    private GetWalletPayoutsQuery.Handler CreateHandler()
    {
        return new GetWalletPayoutsQuery.Handler(UnitOfWork);
    }

    private GetWalletPayoutsQuery.Query CreateQuery(
        long walletId = 1,
        string? userPublicId = null,
        int page = 1,
        int pageSize = 20)
    {
        return new GetWalletPayoutsQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
            WalletId = walletId,
            Page = page,
            PageSize = pageSize,
        };
    }

    private async Task<WalletCategory> SeedCategoryAsync(
        string name = "Savings", string icon = "PiggyBank")
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
            throw new InvalidOperationException("Category required.");

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

    private async Task<Payout> SeedPayoutAsync(
        long walletId,
        string userPublicId = UserPublicId,
        PayoutStatus status = PayoutStatus.Successful,
        PayoutDestination destination = PayoutDestination.Bank,
        decimal amountNaira = 100m,
        decimal feeNaira = 5m,
        decimal netNaira = 95m,
        string? provider = "paystack",
        string? providerReference = null,
        string? failureReason = null,
        DateTimeOffset? initiatedAt = null,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? failedAt = null)
    {
        var payout = new Payout
        {
            WalletId = walletId,
            UserPublicId = userPublicId,
            Reference = $"pay-{Guid.NewGuid():N}",
            Status = status,
            Destination = destination,
            Amount = Money.FromNaira(amountNaira),
            Fee = Money.FromNaira(feeNaira),
            NetAmount = Money.FromNaira(netNaira),
            Provider = provider,
            ProviderReference = providerReference,
            FailureReason = failureReason,
            InitiatedAt = initiatedAt ?? DateTimeOffset.UtcNow,
            CompletedAt = completedAt,
            FailedAt = failedAt,
        };
        await UnitOfWork.AddAsync(payout);
        await UnitOfWork.SaveChangesAsync();
        return payout;
    }

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(userPublicId: ""), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User public ID is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithNonPositiveWalletId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: 0), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid wallet ID.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownWallet_ReturnsNotFound()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: 999_999), default);

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
        var result = await handler.Handle(CreateQuery(walletId: otherWallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Wallet not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithNoPayouts_ReturnsEmptyGroups()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Empty(result.Data!.Items);
        Assert.Equal(0, result.Data.TotalPayouts);
        Assert.Equal(0, result.Data.TotalCount);
        Assert.Equal(0, result.Data.TotalPages);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyPayoutsBelongingToTheWallet()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var otherWallet = await SeedWalletAsync(name: "Other", categoryId: cat.Id);

        await SeedPayoutAsync(wallet.Id, netNaira: 100m);
        await SeedPayoutAsync(otherWallet.Id, netNaira: 999m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data!.TotalPayouts);
        var item = Assert.Single(result.Data.Items.SelectMany(g => g.Activities));
        Assert.Equal(100m, item.Amount);
    }

    [Fact]
    public async Task Handle_ExcludesPayoutsFromOtherUsersOnSameWalletId()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedPayoutAsync(wallet.Id, UserPublicId, netNaira: 100m);
        await SeedPayoutAsync(wallet.Id, OtherUserPublicId, netNaira: 999m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal(100m, item.Amount);
    }

    [Fact]
    public async Task Handle_GroupsByDate()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var today = DateTimeOffset.UtcNow;
        var yesterday = today.AddDays(-1);

        await SeedPayoutAsync(wallet.Id, initiatedAt: today);
        await SeedPayoutAsync(wallet.Id, initiatedAt: today.AddMinutes(-5));
        await SeedPayoutAsync(wallet.Id, initiatedAt: yesterday);
        await SeedPayoutAsync(wallet.Id, initiatedAt: yesterday.AddMinutes(-10));
        await SeedPayoutAsync(wallet.Id, initiatedAt: today.AddMinutes(-15));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

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
        await SeedPayoutAsync(wallet.Id, initiatedAt: today.AddDays(-3));
        await SeedPayoutAsync(wallet.Id, initiatedAt: today);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.True(result.Data!.Items[0].Date > result.Data.Items[1].Date);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectSliceAndMetadata()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var baseTime = DateTimeOffset.UtcNow.AddHours(-10);
        for (var i = 0; i < 7; i++)
        {
            await SeedPayoutAsync(wallet.Id, initiatedAt: baseTime.AddHours(i));
        }

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id, page: 2, pageSize: 3),
            default);

        Assert.Equal(7, result.Data!.TotalPayouts);
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
        await SeedPayoutAsync(wallet.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id, pageSize: 5_000),
            default);

        Assert.Equal(100, result.Data!.PageSize);
    }

    [Fact]
    public async Task Handle_InvalidPageAndPageSize_FallBackToDefaults()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedPayoutAsync(wallet.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id, page: 0, pageSize: 0),
            default);

        Assert.Equal(1, result.Data!.Page);
        Assert.Equal(20, result.Data.PageSize);
    }

    [Fact]
    public async Task Handle_SuccessfulBankPayout_HasExpectedTitleAndSubtitle()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedPayoutAsync(
            wallet.Id,
            status: PayoutStatus.Successful,
            destination: PayoutDestination.Bank,
            providerReference: "REF-123",
            initiatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            completedAt: DateTimeOffset.UtcNow);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal("Bank Payout Successful", item.Title);
        Assert.Equal("Ref: REF-123", item.Subtitle);
        Assert.True(item.IsCredit);
        Assert.Equal("bank", item.Destination);
        Assert.Equal(nameof(PayoutStatus.Successful), item.Type);
    }

    [Fact]
    public async Task Handle_PendingBankPayout_HasPendingSubtitle()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedPayoutAsync(
            wallet.Id,
            status: PayoutStatus.Pending,
            destination: PayoutDestination.Bank);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal("Bank Payout Pending", item.Title);
        Assert.Equal("Waiting to be sent to your bank", item.Subtitle);
        Assert.False(item.IsCredit);
    }

    [Fact]
    public async Task Handle_FailedBankPayout_UsesFailureReasonAsSubtitle()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedPayoutAsync(
            wallet.Id,
            status: PayoutStatus.Failed,
            destination: PayoutDestination.Bank,
            failureReason: "Invalid account number");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal("Bank Payout Failed", item.Title);
        Assert.Equal("Invalid account number", item.Subtitle);
    }

    [Fact]
    public async Task Handle_SuccessfulMainPayout_ShowsMainBalanceSubtitle()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedPayoutAsync(
            wallet.Id,
            status: PayoutStatus.Successful,
            destination: PayoutDestination.Main);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal("Main Balance Payout Successful", item.Title);
        Assert.Equal("Added to your main MOVA balance", item.Subtitle);
        Assert.Equal("main", item.Destination);
    }

    [Fact]
    public async Task Handle_ReversedBankPayout_HasReversalSubtitle()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedPayoutAsync(
            wallet.Id,
            status: PayoutStatus.Reversed,
            destination: PayoutDestination.Bank);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal(
            "Bank payout was reversed and returned to your wallet",
            item.Subtitle);
    }

    [Fact]
    public async Task Handle_Amount_UsesNetAmountNotGross()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        await SeedPayoutAsync(
            wallet.Id,
            amountNaira: 1000m,
            feeNaira: 50m,
            netNaira: 950m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal(950m, item.Amount);
    }

    [Fact]
    public async Task Handle_Timestamp_PrefersCompletedAtThenFailedAtThenInitiatedAt()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var completed = DateTimeOffset.UtcNow;
        var initiated = completed.AddMinutes(-10);

        await SeedPayoutAsync(
            wallet.Id,
            status: PayoutStatus.Successful,
            initiatedAt: initiated,
            completedAt: completed);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal(completed, item.Date);
    }

    [Fact]
    public async Task Handle_Timestamp_FallsBackToInitiatedAtWhenNotCompletedOrFailed()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var initiated = DateTimeOffset.UtcNow.AddMinutes(-5);

        await SeedPayoutAsync(
            wallet.Id,
            status: PayoutStatus.Processing,
            initiatedAt: initiated,
            completedAt: null,
            failedAt: null);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var item = Assert.Single(result.Data!.Items.SelectMany(g => g.Activities));
        Assert.Equal(initiated, item.Date);
    }
}