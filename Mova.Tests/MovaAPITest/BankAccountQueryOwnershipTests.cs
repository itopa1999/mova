using System.Net;
using Moq;
using Mova.Application.BBL.Queries.BanksAccount;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;
using BankAccountsQuery = Mova.Application.BBL.Queries.BanksAccount.GetAllBankAccount;
using DepositHistoryQuery = Mova.Application.BBL.Queries.BanksAccount.DepositTransaction;
using TransactionsQuery = Mova.Application.BBL.Queries.BanksAccount.GetTransactions;
using FundingAccountQuery = Mova.Application.BBL.Queries.BanksAccount.GetAccountForFunding;
using CallbackQuery = Mova.Application.BBL.Queries.BanksAccount.PaymentCallback;

namespace Mova.Tests.Handlers;

public sealed class BankAccountQueryOwnershipTests : BaseTest
{
    private const string UserPublicId = "bank-query-owner";
    private const string OtherUserPublicId = "bank-query-other";

    [Fact]
    public async Task GetAllBankAccounts_ReturnsOnlyActiveAccountsOwnedByRequestingUser()
    {
        await SeedBankAccountAsync(UserPublicId, "1111111111", BankAccountStatus.Active);
        await SeedBankAccountAsync(OtherUserPublicId, "2222222222", BankAccountStatus.Active);
        await SeedBankAccountAsync(UserPublicId, "3333333333", BankAccountStatus.Suspended);

        var handler = new BankAccountsQuery.Handler(UnitOfWork);
        var result = await handler.Handle(
            new BankAccountsQuery.Query { UserPublicId = UserPublicId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var account = Assert.Single(result.Data!);
        Assert.Equal("1111111111", account.AccountNumber);
        Assert.Equal("Owner account", account.AccountName);
    }

    [Fact]
    public async Task GetTransactions_ReturnsOnlyRequestingUsersMatchingTransactions()
    {
        await SeedTransactionAsync(UserPublicId, "Owner transaction", TransactionType.Deposit);
        await SeedTransactionAsync(OtherUserPublicId, "Other transaction", TransactionType.Deposit);

        var handler = new TransactionsQuery.Handler(UnitOfWork);
        var result = await handler.Handle(
            new TransactionsQuery.Query { UserPublicId = UserPublicId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var transaction = Assert.Single(result.Data!.Items);
        Assert.Equal("Owner transaction", transaction.Title);
        Assert.Equal(125m, transaction.Amount);
        Assert.Equal(nameof(TransactionType.Deposit), transaction.Type);
        Assert.Equal(1, result.Data.TotalItems);
    }

    [Fact]
    public async Task DepositTransactions_ExcludeOtherUsersAndWalletTransactions()
    {
        var category = new WalletCategory { Name = "Savings", Icon = "PiggyBank" };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();
        var wallet = new Wallet
        {
            UserPublicId = UserPublicId,
            CategoryId = category.Id,
            Name = "Owner wallet",
            TargetAmount = Money.FromNaira(500m),
            LockedAmount = Money.FromNaira(500m),
            FundedAmount = Money.FromNaira(500m),
            Status = WalletStatus.Active
        };
        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();

        await SeedTransactionAsync(UserPublicId, "Owner deposit", TransactionType.Deposit);
        await SeedTransactionAsync(OtherUserPublicId, "Other deposit", TransactionType.Deposit);
        await SeedTransactionAsync(
            UserPublicId,
            "Wallet transaction",
            TransactionType.Deposit,
            walletId: wallet.Id);

        var handler = new DepositHistoryQuery.Handler(UnitOfWork);
        var result = await handler.Handle(
            new DepositHistoryQuery.Query { UserPublicId = UserPublicId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var transaction = Assert.Single(result.Data!);
        Assert.Equal("Owner deposit", transaction.Title);
        Assert.Equal(TransactionType.Deposit, transaction.Type);
    }

    [Fact]
    public async Task FundingAccount_ReturnsOnlyTheActiveVirtualAccountOwnedByRequestingUser()
    {
        await UnitOfWork.AddAsync(new VirtualAccount
        {
            UserPublicId = UserPublicId,
            Provider = PaymentProvider.Paystack,
            AccountNumber = "1111111111",
            AccountName = "Owner virtual account",
            BankName = "Owner Bank",
            Status = VirtualAccountStatus.Active
        });
        await UnitOfWork.AddAsync(new VirtualAccount
        {
            UserPublicId = OtherUserPublicId,
            Provider = PaymentProvider.Paystack,
            AccountNumber = "2222222222",
            AccountName = "Other virtual account",
            BankName = "Other Bank",
            Status = VirtualAccountStatus.Active
        });
        await UnitOfWork.SaveChangesAsync();

        var featureFlags = new Mock<IFeatureFlagService>();
        featureFlags
            .Setup(x => x.IsEnabledAsync(
                It.IsAny<FeatureFlagName>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = new FundingAccountQuery.Handler(UnitOfWork, featureFlags.Object);

        var result = await handler.Handle(
            new FundingAccountQuery.Query { UserPublicId = UserPublicId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.IsDepositAllowed);
        Assert.Equal(new[] { nameof(PaymentProvider.Paystack), nameof(PaymentProvider.Monnify), nameof(PaymentProvider.Flutterwave) }, result.Data.AllowedGateways);
        Assert.Equal("1111111111", result.Data.AccountDetails!.AccountNumber);
        Assert.Equal("Owner virtual account", result.Data.AccountDetails.AccountName);
        Assert.Equal("Owner Bank", result.Data.AccountDetails.BankName);
    }

    [Fact]
    public async Task PaymentCallback_ReturnsTheTransactionMatchingReference()
    {
        await SeedTransactionAsync(UserPublicId, "Payment", TransactionType.Deposit);
        var transaction = Context.Transactions.Single();
        transaction.Reference = "payment-reference";
        await UnitOfWork.SaveChangesAsync();

        var handler = new CallbackQuery.Handler(UnitOfWork);
        var result = await handler.Handle(
            new CallbackQuery.Query { Reference = "payment-reference" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("payment-reference", result.Data!.Reference);
        Assert.Equal(nameof(TransactionStatus.Completed), result.Data.Status);
        Assert.Equal(125m, result.Data.Amount);
        Assert.NotNull(result.Data.CreatedAt);
    }

    private async Task SeedBankAccountAsync(
        string userPublicId,
        string accountNumber,
        BankAccountStatus status)
    {
        await UnitOfWork.AddAsync(new BankAccount
        {
            UserPublicId = userPublicId,
            AccountNumber = accountNumber,
            AccountName = "Owner account",
            BankCode = "058",
            BankName = "Example Bank",
            BankImageUrl = "example.png",
            Status = status
        });
        await UnitOfWork.SaveChangesAsync();
    }

    private async Task SeedTransactionAsync(
        string userPublicId,
        string title,
        TransactionType type,
        long? walletId = null)
    {
        await UnitOfWork.AddAsync(new Transaction
        {
            UserPublicId = userPublicId,
            WalletId = walletId,
            Title = title,
            Amount = Money.FromNaira(125m),
            Type = type,
            Status = TransactionStatus.Completed,
            Reference = $"ref-{title.Replace(' ', '-')}"
        });
        await UnitOfWork.SaveChangesAsync();
    }
}
