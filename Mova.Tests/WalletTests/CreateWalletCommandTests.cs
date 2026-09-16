using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class CreateWalletCommandTests : BaseTest
{
    private const string UserPublicId = "user_create_test";
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<ISchedulePreviewService> _schedulePreview = new();
    private readonly Mock<IWalletRuleService> _walletRuleService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private CreateWalletCommand.Handler CreateHandler()
    {
        return new CreateWalletCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            Mock.Of<ILogger<CreateWalletCommand.Handler>>(),
            _schedulePreview.Object,
            _walletRuleService.Object,
            _notifications.Object);
    }

    private CreateWalletCommand.Command CreateCommand(
        long categoryId,
        long bankAccountId,
        string name = "Rent Savings")
    {
        return new CreateWalletCommand.Command
        {
            UserPublicId = UserPublicId,
            Email = UserEmail,
            FirstName = UserFirstName,
            Name = name,
            Description = "Monthly rent",
            CategoryId = categoryId,
            BankAccountId = bankAccountId,
            TargetAmount = 30_000m,
            Frequency = ReleaseFrequency.Daily,
            FrequencyConfig = "{}",
            AmountToBeReleased = 1_000m,
            StartDate = DateTimeOffset.UtcNow.Date.AddDays(1),
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

    private void SetupHappyPathDependencies()
    {
        _identityService
            .Setup(x => x.DebitBalanceAsync(
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _schedulePreview
            .Setup(x => x.PreviewScheduleAsync(
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<ReleaseFrequency>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulePreviewResult
            {
                IsSuccess = true,
                TotalReleases = 30,
            });

        _walletRuleService
            .Setup(x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletRule rule, DateTimeOffset after, CancellationToken _) =>
                new NextWalletRelease
                {
                    ScheduledFor = after.AddDays(1),
                    Amount = rule.Amount,
                });
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_CreatesWallet()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.WalletId > 0);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == result.Data.WalletId);

        Assert.NotNull(wallet);
        Assert.Equal("Rent Savings", wallet!.Name);
        Assert.Equal(WalletStatus.Active, wallet.Status);
        Assert.Equal(30_000m, wallet.TargetAmount.ToDecimal());
        Assert.Equal(30_000m, wallet.LockedAmount.ToDecimal());
        Assert.Equal(0m, wallet.AvailableAmount.ToDecimal());
    }

    [Fact]
    public async Task Handle_WithValidRequest_DebitsBalanceExactlyOnce()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(category.Id, bank.Id), default);

        _identityService.Verify(
            x => x.DebitBalanceAsync(
                UserPublicId,
                30_000m,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CreatesWalletCreatedTransaction()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var walletId = result.Data!.WalletId;

        var tx = await Context.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Reference == $"wallet-created:{walletId}");

        Assert.NotNull(tx);
        Assert.Equal(TransactionType.Deposit, tx!.Type);
        Assert.Equal(TransactionStatus.Completed, tx.Status);
        Assert.Equal(30_000m, tx.Amount.ToDecimal());
    }

    [Fact]
    public async Task Handle_WithValidRequest_CreatesWalletRule()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var rule = await Context.WalletRules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WalletId == result.Data!.WalletId);

        Assert.NotNull(rule);
        Assert.Equal(1_000m, rule!.Amount.ToDecimal());
        Assert.Equal(ReleaseFrequency.Daily, rule.Frequency);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SchedulesFirstRelease()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var release = await Context.ScheduledReleases
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WalletId == result.Data!.WalletId);

        Assert.NotNull(release);
        Assert.Equal(ReleaseStatus.Scheduled, release!.Status);
        Assert.True(release.ScheduledFor > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CommitsTransactionExactlyOnce()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(category.Id, bank.Id), default);

        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.CommitCount);
        Assert.Equal(0, UnitOfWork.RollbackCount);
    }

    // ---------------------------------------------------------
    // 2. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyName_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.Name = string.Empty;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Handle_WithNameTooLong_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.Name = new string('a', 151);

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithTargetBelowMinimum_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.TargetAmount = 1_000m;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithReleaseAmountBelowMinimum_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.AmountToBeReleased = 50m;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithReleaseGreaterThanTarget_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.AmountToBeReleased = 40_000m;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithDuplicateName_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var existing = new Wallet
        {
            UserPublicId = UserPublicId,
            Name = "Rent Savings",
            CategoryId = category.Id,
            BankAccountId = bank.Id,
            TargetAmount = Money.FromNaira(30_000m),
            FundedAmount = Money.FromNaira(30_000m),
            LockedAmount = Money.FromNaira(30_000m),
            AvailableAmount = Money.FromNaira(0),
            UnusedAmount = Money.FromNaira(0),
            TotalReleasedAmount = Money.FromNaira(0),
            Status = WalletStatus.Active,
        };
        await UnitOfWork.AddAsync(existing);
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id, name: "Rent Savings"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithUnknownCategory_ReturnsBadRequest()
    {
        var (_, bank) = await SeedPrerequisitesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: 999_999L, bankAccountId: bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithUnknownBankAccount_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: category.Id, bankAccountId: 999_999L), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithBankAccountLackingConsent_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        bank.ConsentGiven = false;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithInactiveBankAccount_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        bank.Status = BankAccountStatus.Suspended;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithInsufficientBalance_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        _identityService
            .Setup(x => x.DebitBalanceAsync(
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WithInvalidSchedulePreview_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        _schedulePreview
            .Setup(x => x.PreviewScheduleAsync(
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<ReleaseFrequency>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulePreviewResult
            {
                IsSuccess = false,
                Errors = new List<string> { "Invalid schedule" },
            });

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    // ---------------------------------------------------------
    // 3. REGRESSION: notification failures must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillReturnsSuccess()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

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
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.True(result.IsSuccess);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == result.Data!.WalletId);

        Assert.NotNull(wallet);
    }

    [Fact]
    public async Task Handle_WhenEmailQueueThrows_StillReturnsSuccess()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Hangfire unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WhenBothNotificationsThrow_StillReturnsSuccess()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

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
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WhenInAppThrows_EmailIsStillAttempted()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

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
        await handler.Handle(CreateCommand(category.Id, bank.Id), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 4. REGRESSION: notification content
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_SendsNotificationWithCorrectContent()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(category.Id, bank.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                "Rent Savings wallet created",
                It.Is<string>(m => m.Contains("Rent Savings")),
                "/wallets",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m => m.Contains("Rent Savings")),
                "Your Rent Savings wallet is ready"),
            Times.Once);
    }
}