using System.Net;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using GetWalletBankAccountQuery = Mova.Application.BBL.Queries.AccountWallet.GetWalletBankAccount;

namespace Mova.Tests.Handlers;

public sealed class GetWalletBankAccountQueryTests : BaseTest
{
    private const string UserPublicId = "user_wallet_bank_account";
    private const string OtherUserPublicId = "someone_else";

    private GetWalletBankAccountQuery.Handler CreateHandler()
    {
        return new GetWalletBankAccountQuery.Handler(UnitOfWork);
    }

    private GetWalletBankAccountQuery.Query CreateQuery(
        long walletId = 1,
        string? userPublicId = null)
    {
        return new GetWalletBankAccountQuery.Query
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

    private async Task<BankAccount> SeedBankAccountAsync(
        string userPublicId = UserPublicId,
        string accountName = "Lucky Starboy",
        string accountNumber = "0123456789",
        string bankCode = "058",
        string bankName = "Example Bank",
        string bankImageUrl = "example.png",
        BankAccountStatus status = BankAccountStatus.Active)
    {
        var account = new BankAccount
        {
            UserPublicId = userPublicId,
            AccountName = accountName,
            AccountNumber = accountNumber,
            BankCode = bankCode,
            BankName = bankName,
            BankImageUrl = bankImageUrl,
            Status = status,
        };

        await UnitOfWork.AddAsync(account);
        await UnitOfWork.SaveChangesAsync();
        return account;
    }

    private async Task<Wallet> SeedWalletAsync(
        string userPublicId = UserPublicId,
        string name = "Rent",
        long? categoryId = null,
        long? bankAccountId = null)
    {
        if (categoryId is null || categoryId <= 0)
            throw new InvalidOperationException(
                "Wallet.CategoryId is required and must reference a persisted WalletCategory.");

        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            Name = name,
            CategoryId = categoryId.Value,
            BankAccountId = bankAccountId,
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
        var bank = await SeedBankAccountAsync(OtherUserPublicId);
        var otherWallet = await SeedWalletAsync(
            OtherUserPublicId, "Theirs", cat.Id, bank.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: otherWallet.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Wallet not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithWalletOwnedByOtherUser_DoesNotLeakBankAccount()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync(
            OtherUserPublicId,
            accountName: "Secret Holder",
            accountNumber: "9999999999");
        var otherWallet = await SeedWalletAsync(
            OtherUserPublicId, "Theirs", cat.Id, bank.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: otherWallet.Id),
            default);

        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Handle_WithNoLinkedBankAccount_ReturnsOkWithNullData()
    {
        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, bankAccountId: null);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Null(result.Data);
        Assert.Equal("No bank account is linked to this wallet.", result.Message);
    }

    [Fact]
    public async Task Handle_WithLinkedBankAccount_ReturnsAccountDetails()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync(
            accountName: "Lucky Starboy",
            accountNumber: "0123456789",
            bankName: "Example Bank",
            bankImageUrl: "example.png");
        var wallet = await SeedWalletAsync(categoryId: cat.Id, bankAccountId: bank.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal(bank.Id, result.Data!.Id);
        Assert.Equal("Lucky Starboy", result.Data.AccountName);
        Assert.Equal("0123456789", result.Data.AccountNumber);
        Assert.Equal("Example Bank", result.Data.BankName);
        Assert.Equal("example.png", result.Data.BankImageUrl);
        Assert.Equal("Wallet bank account retrieved successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheBankAccountLinkedToTheRequestedWallet()
    {
        var cat = await SeedCategoryAsync();

        var firstBank = await SeedBankAccountAsync(
            accountName: "First Holder",
            accountNumber: "1111111111");
        var secondBank = await SeedBankAccountAsync(
            accountName: "Second Holder",
            accountNumber: "2222222222");

        var walletWithFirst = await SeedWalletAsync(
            name: "W1", categoryId: cat.Id, bankAccountId: firstBank.Id);
        var walletWithSecond = await SeedWalletAsync(
            name: "W2", categoryId: cat.Id, bankAccountId: secondBank.Id);

        var handler = CreateHandler();

        var firstResult = await handler.Handle(
            CreateQuery(walletId: walletWithFirst.Id),
            default);

        var secondResult = await handler.Handle(
            CreateQuery(walletId: walletWithSecond.Id),
            default);

        Assert.Equal(firstBank.Id, firstResult.Data!.Id);
        Assert.Equal("First Holder", firstResult.Data.AccountName);

        Assert.Equal(secondBank.Id, secondResult.Data!.Id);
        Assert.Equal("Second Holder", secondResult.Data.AccountName);
    }

    [Fact]
    public async Task Handle_WithSuspendedBankAccount_StillReturnsDetails()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync(
            status: BankAccountStatus.Suspended);
        var wallet = await SeedWalletAsync(categoryId: cat.Id, bankAccountId: bank.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(walletId: wallet.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal(bank.Id, result.Data!.Id);
    }
}