using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class BreakWalletCommandTests : BaseTest
{
    private const string UserPublicId = "user_break_test";
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";

    private readonly Mock<INotificationQueue> _notifications = new();

    private BreakWalletCommand.Handler CreateHandler()
    {
        return new BreakWalletCommand.Handler(
            UnitOfWork,
            Mock.Of<MediatR.IMediator>(),
            Mock.Of<ILogger<BreakWalletCommand.Handler>>(),
            _notifications.Object);
    }

    private BreakWalletCommand.Command CreateCommand(long walletId)
    {
        return new BreakWalletCommand.Command
        {
            UserPublicId = UserPublicId,
            Email = UserEmail,
            FirstName = UserFirstName,
            WalletId = walletId,
        };
    }

    private async Task<(WalletCategory category, BankAccount bank)> SeedPrerequisitesAsync()
    {
        var category = new WalletCategory
        {
            Name = "Rent & Housing",
            Icon = "Wallet",
        };

        var bank = new BankAccount
        {
            UserPublicId = UserPublicId,
            AccountNumber = "0123456789",
            AccountName = "Lucky Starboy",
            BankCode = "058",
            BankName = "GTBank",
            BankImageUrl = "https://example.com/gtb.png",
            Status = BankAccountStatus.Active,
            IsDefault = true,
            ConsentGiven = true,
            ConsentGivenAt = DateTimeOffset.UtcNow,
            ConsentVersion = "v1",
            Currency = "NGN",
            VerifiedAt = DateTimeOffset.UtcNow,
            VerificationMessage = "Verified",
        };

        await UnitOfWork.AddAsync(category);
        await UnitOfWork.AddAsync(bank);
        await UnitOfWork.SaveChangesAsync();

        return (category, bank);
    }

    private async Task<Wallet> SeedActiveWalletAsync(
        decimal locked,
        decimal available = 0,
        decimal unused = 0)
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var wallet = new Wallet
        {
            UserPublicId = UserPublicId,
            Name = "Rent Savings",
            Description = "Monthly rent",
            CategoryId = category.Id,
            BankAccountId = bank.Id,
            TargetAmount = Money.FromNaira(locked),
            FundedAmount = Money.FromNaira(locked),
            LockedAmount = Money.FromNaira(locked),
            AvailableAmount = Money.FromNaira(available),
            UnusedAmount = Money.FromNaira(unused),
            TotalReleasedAmount = Money.FromNaira(0),
            Status = WalletStatus.Active,
        };

        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();

        return wallet;
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithActiveWallet_MarksWalletBroken()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.True(result.IsSuccess);

        var updated = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(WalletStatus.Broken, updated.Status);
        Assert.Equal(0, updated.LockedAmount.MinorUnits);
        Assert.Equal(0, updated.AvailableAmount.MinorUnits);
        Assert.Equal(0, updated.UnusedAmount.MinorUnits);
    }

    [Fact]
    public async Task Handle_WithActiveWallet_CreatesBreakAndFeeTransactions()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);
        var handler = CreateHandler();

        await handler.Handle(CreateCommand(wallet.Id), default);

        var transactions = await Context.Transactions
            .AsNoTracking()
            .Where(x => x.WalletId == wallet.Id)
            .ToListAsync();

        Assert.Equal(2, transactions.Count);

        var breakTx = transactions.Single(x => x.Reference == $"wallet-break:{wallet.Id}");
        Assert.Equal(TransactionType.Refund, breakTx.Type);
        Assert.Equal(29_400m, breakTx.Amount.ToDecimal());

        var feeTx = transactions.Single(x => x.Reference == $"wallet-break-fee:{wallet.Id}");
        Assert.Equal(TransactionType.Fee, feeTx.Type);
        Assert.Equal(600m, feeTx.Amount.ToDecimal());
        Assert.Equal(TransactionStatus.Completed, feeTx.Status);
    }

    [Fact]
    public async Task Handle_WithActiveWallet_CancelsPendingReleases()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);

        var rule = new WalletRule
        {
            WalletId = wallet.Id,
            Amount = Money.FromNaira(1_000m),
            Frequency = ReleaseFrequency.Daily,
            FrequencyConfig = "{}",
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddDays(30),
        };
        await UnitOfWork.AddAsync(rule);
        await UnitOfWork.SaveChangesAsync();

        var release = new ScheduledRelease
        {
            WalletId = wallet.Id,
            WalletRuleId = rule.Id,
            Amount = Money.FromNaira(1_000m),
            ScheduledFor = DateTimeOffset.UtcNow.AddDays(1),
            Status = ReleaseStatus.Scheduled,
        };
        await UnitOfWork.AddAsync(release);
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id), default);

        var updated = await Context.ScheduledReleases
            .AsNoTracking()
            .FirstAsync(x => x.Id == release.Id);

        Assert.Equal(ReleaseStatus.Cancelled, updated.Status);
    }

    [Fact]
    public async Task Handle_WithActiveWallet_CommitsTransactionExactlyOnce()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);
        var handler = CreateHandler();

        await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.CommitCount);
        Assert.Equal(0, UnitOfWork.RollbackCount);
    }

    // ---------------------------------------------------------
    // 2. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithUnknownWallet_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(999_999L), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal(1, UnitOfWork.RollbackCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Handle_WithWalletBelongingToAnotherUser_ReturnsNotFound()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);

        var command = new BreakWalletCommand.Command
        {
            UserPublicId = "someone_else",
            Email = UserEmail,
            FirstName = UserFirstName,
            WalletId = wallet.Id,
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, result.StatusCode);

        var unchanged = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(WalletStatus.Active, unchanged.Status);
    }

    [Fact]
    public async Task Handle_WithPausedWallet_ReturnsBadRequest()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);

        wallet.Status = WalletStatus.Paused;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WithZeroBalanceWallet_ReturnsBadRequest()
    {
        var wallet = await SeedActiveWalletAsync(locked: 0m);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WhenAlreadyBroken_ReturnsConflict()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);

        var existing = new Transaction
        {
            UserPublicId = UserPublicId,
            WalletId = wallet.Id,
            Title = "Wallet Broken",
            Amount = Money.FromNaira(29_400m),
            Type = TransactionType.Refund,
            Status = TransactionStatus.Processing,
            Reference = $"wallet-break:{wallet.Id}",
        };
        await UnitOfWork.AddAsync(existing);
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    // ---------------------------------------------------------
    // 3. REGRESSION: notification failures must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillReturnsSuccess()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);

        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Redis down"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.True(result.IsSuccess);

        var updated = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(WalletStatus.Broken, updated.Status);
    }

    [Fact]
    public async Task Handle_WhenEmailQueueThrows_StillReturnsSuccess()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);

        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Hangfire unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.True(result.IsSuccess);

        var updated = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(WalletStatus.Broken, updated.Status);
    }

    [Fact]
    public async Task Handle_WhenBothNotificationsThrow_StillReturnsSuccess()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);

        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Exception("boom"));

        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new Exception("boom"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.True(result.IsSuccess);

        var updated = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(WalletStatus.Broken, updated.Status);
    }

    [Fact]
    public async Task Handle_WhenInAppThrows_EmailIsStillAttempted()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);

        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Exception("boom"));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailMissing_SkipsEmailButStillSucceeds()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);

        var command = new BreakWalletCommand.Command
        {
            UserPublicId = UserPublicId,
            Email = string.Empty,
            FirstName = UserFirstName,
            WalletId = wallet.Id,
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 4. REGRESSION: notification content is correct
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithActiveWallet_SendsNotificationWithCorrectAmounts()
    {
        var wallet = await SeedActiveWalletAsync(locked: 30_000m);
        var handler = CreateHandler();

        await handler.Handle(CreateCommand(wallet.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                "Rent Savings wallet broken",
                It.Is<string>(m => m.Contains("29,400")),
                $"/wallet/{wallet.Id}",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m => m.Contains("29,400") && m.Contains("600")),
                "Your Rent Savings wallet has been broken"),
            Times.Once);
    }
}