using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.BanksAccount;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Tests.BankAccountTest;

public sealed class WithdrawalCommandTests : BaseTest
{
    private const string OwnerPublicId = "withdrawal-owner";
    private const string OtherUserPublicId = "withdrawal-other";
    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    [Fact]
    public async Task Handle_WhenBalanceIsSufficient_DebitsWalletAndRecordsProcessingWithdrawal()
    {
        var wallet = await SeedWalletAsync(OwnerPublicId, "Available wallet", 3_000m);
        SetupUserForNotifications();
        var handler = CreateHandler(withdrawalsEnabled: true);

        var result = await handler.Handle(
            new WithdrawalCommand.Command
            {
                UserPublicId = OwnerPublicId,
                WalletId = wallet.Id,
                Amount = 2_500m,
                Type = "bank",
                BankAccountId = 12
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("processing", result.Data!.Status);
        Assert.StartsWith("WDL-", result.Data.Reference);

        var updatedWallet = await Context.Wallets.FindAsync(wallet.Id);
        Assert.NotNull(updatedWallet);
        Assert.Equal(500m, updatedWallet.AvailableAmount.ToDecimal());
        Assert.Equal(2_500m, updatedWallet.TotalWithdrawnAmount.ToDecimal());

        var transaction = Assert.Single(Context.Transactions);
        Assert.Equal(wallet.Id, transaction.WalletId);
        Assert.Equal(OwnerPublicId, transaction.UserPublicId);
        Assert.Equal(TransactionType.Withdrawal, transaction.Type);
        Assert.Equal(TransactionStatus.Processing, transaction.Status);
        Assert.Null(transaction.Provider);
        Assert.Equal(2_500m, transaction.Amount.ToDecimal());
        Assert.Equal(result.Data.Reference, transaction.Reference);

        var ledgerEntry = Assert.Single(Context.LedgerEntries);
        Assert.Equal(wallet.Id, ledgerEntry.WalletId);
        Assert.Equal(transaction.Id, ledgerEntry.TransactionId);
        Assert.False(ledgerEntry.IsCredit);
        Assert.Equal(2_500m, ledgerEntry.Amount.ToDecimal());

        _notifications.Verify(
            queue => queue.InAppNotificationAsync(
                OwnerPublicId,
                NotificationType.Wallet,
                "Withdrawal initiated",
                It.Is<string>(message => message.Contains(result.Data.Reference)),
                "/transactions",
                null,
                CancellationToken.None),
            Times.Once);
        _notifications.Verify(
            queue => queue.QueueNotificationEmail(
                "Mova",
                "mova@example.com",
                It.Is<string>(message => message.Contains(result.Data.Reference)),
                "Your MOVA withdrawal has been initiated"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenBalanceIsInsufficient_DoesNotDebitOrRecordWithdrawal()
    {
        var wallet = await SeedWalletAsync(OwnerPublicId, "Available wallet", 500m);
        var handler = CreateHandler(withdrawalsEnabled: true);

        var result = await handler.Handle(
            new WithdrawalCommand.Command
            {
                UserPublicId = OwnerPublicId,
                WalletId = wallet.Id,
                Amount = 500.01m,
                Type = "utilities",
                UtilityType = "airtime",
                Network = "mtn",
                PhoneNumber = "08031234567"
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(500m, wallet.AvailableAmount.ToDecimal());
        Assert.Empty(Context.Transactions);
        Assert.Empty(Context.LedgerEntries);
        VerifyNoWithdrawalNotifications();
    }

    [Fact]
    public async Task Handle_WhenWalletBelongsToAnotherUser_ReturnsNotFoundWithoutDebit()
    {
        var wallet = await SeedWalletAsync(OtherUserPublicId, "Private wallet", 1_000m);
        var handler = CreateHandler(withdrawalsEnabled: true);

        var result = await handler.Handle(
            new WithdrawalCommand.Command
            {
                UserPublicId = OwnerPublicId,
                WalletId = wallet.Id,
                Amount = 100m,
                Type = "bank",
                BankAccountId = 12
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal(1_000m, wallet.AvailableAmount.ToDecimal());
        Assert.Empty(Context.Transactions);
        Assert.Empty(Context.LedgerEntries);
        VerifyNoWithdrawalNotifications();
    }

    [Fact]
    public async Task Handle_WhenWithdrawalsAreDisabled_DoesNotDebitWallet()
    {
        var wallet = await SeedWalletAsync(OwnerPublicId, "Available wallet", 1_000m);
        var handler = CreateHandler(withdrawalsEnabled: false);

        var result = await handler.Handle(
            new WithdrawalCommand.Command
            {
                UserPublicId = OwnerPublicId,
                WalletId = wallet.Id,
                Amount = 100m,
                Type = "bank",
                BankAccountId = 12
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(1_000m, wallet.AvailableAmount.ToDecimal());
        Assert.Empty(Context.Transactions);
        VerifyNoWithdrawalNotifications();
    }

    [Fact]
    public async Task Handle_WhenNotificationQueueFails_StillReturnsSuccessfulWithdrawal()
    {
        var wallet = await SeedWalletAsync(OwnerPublicId, "Available wallet", 1_000m);
        SetupUserForNotifications();
        _notifications
            .Setup(queue => queue.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Notification queue unavailable"));

        var result = await CreateHandler(withdrawalsEnabled: true).Handle(
            new WithdrawalCommand.Command
            {
                UserPublicId = OwnerPublicId,
                WalletId = wallet.Id,
                Amount = 100m,
                Type = "bank",
                BankAccountId = 12
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(900m, wallet.AvailableAmount.ToDecimal());
        _notifications.Verify(
            queue => queue.QueueNotificationEmail(
                "Mova",
                "mova@example.com",
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    [Theory]
    [InlineData("airtime")]
    [InlineData("data")]
    [InlineData("cable")]
    [InlineData("electricity")]
    public void UtilityWithdrawalRequests_ValidateFieldsSentByFrontend(string utilityType)
    {
        var request = new WithdrawalCommand.Command
        {
            WalletId = 5,
            Amount = 500m,
            Type = "utilities",
            UtilityType = utilityType,
            Network = utilityType is "airtime" or "data" ? "mtn" : null,
            PhoneNumber = utilityType is "airtime" or "data" ? "08031234567" : null,
            PlanCode = utilityType == "data" ? "mtn-2gb-7d" : null,
            CableProvider = utilityType == "cable" ? "DSTV" : null,
            SmartcardNumber = utilityType == "cable" ? "1234567890" : null,
            PackageCode = utilityType == "cable" ? "dstv-yanga" : null,
            Disco = utilityType == "electricity" ? "Ikeja Electric" : null,
            MeterNumber = utilityType == "electricity" ? "12345678901" : null,
            MeterType = utilityType == "electricity" ? "prepaid" : null
        };

        Validator.ValidateObject(
            request,
            new ValidationContext(request),
            validateAllProperties: true);
    }

    private WithdrawalCommand.Handler CreateHandler(bool withdrawalsEnabled)
    {
        var featureFlags = new Mock<IFeatureFlagService>();
        featureFlags
            .Setup(service => service.IsEnabledAsync(
                FeatureFlagName.AllowWithdrawFunds,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(withdrawalsEnabled);

        return new WithdrawalCommand.Handler(
            UnitOfWork,
            featureFlags.Object,
            _identityService.Object,
            _notifications.Object,
            Mock.Of<ILogger<WithdrawalCommand.Handler>>());
    }

    private void SetupUserForNotifications()
    {
        _identityService
            .Setup(service => service.GetByIdentifierAsync(
                OwnerPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                1,
                OwnerPublicId,
                "Mova",
                null,
                "User",
                "mova@example.com",
                "08031234567",
                null,
                Money.FromNaira(0m),
                string.Empty,
                false,
                false,
                false,
                false,
                string.Empty,
                DateTimeOffset.UtcNow));
    }

    private void VerifyNoWithdrawalNotifications()
    {
        _notifications.Verify(
            queue => queue.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _notifications.Verify(
            queue => queue.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    private async Task<Wallet> SeedWalletAsync(
        string userPublicId,
        string name,
        decimal availableAmount)
    {
        var category = new WalletCategory
        {
            Name = "Savings",
            Icon = "PiggyBank"
        };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();

        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            CategoryId = category.Id,
            Name = name,
            TargetAmount = Money.FromNaira(10_000m),
            AvailableAmount = Money.FromNaira(availableAmount),
            LockedAmount = Money.FromNaira(0m),
            FundedAmount = Money.FromNaira(availableAmount),
            Status = WalletStatus.Active
        };
        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();
        return wallet;
    }
}
