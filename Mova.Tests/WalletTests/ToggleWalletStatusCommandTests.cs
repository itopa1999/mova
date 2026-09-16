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

public sealed class ToggleWalletStatusCommandTests : BaseTest
{
    private const string UserPublicId = "user_toggle_test";

    private readonly Mock<INotificationQueue> _notifications = new();

    private ToggleWalletStatusCommand.Handler CreateHandler()
    {
        return new ToggleWalletStatusCommand.Handler(
            UnitOfWork,
            Mock.Of<ILogger<ToggleWalletStatusCommand.Handler>>(),
            _notifications.Object);
    }

    private ToggleWalletStatusCommand.Command CreateCommand(long walletId)
    {
        return new ToggleWalletStatusCommand.Command
        {
            UserPublicId = UserPublicId,
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

    private async Task<Wallet> SeedWalletAsync(
        WalletStatus status = WalletStatus.Active,
        string name = "Rent Savings")
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var wallet = new Wallet
        {
            UserPublicId = UserPublicId,
            Name = name,
            Description = "Monthly rent",
            CategoryId = category.Id,
            BankAccountId = bank.Id,
            TargetAmount = Money.FromNaira(30_000m),
            FundedAmount = Money.FromNaira(30_000m),
            LockedAmount = Money.FromNaira(30_000m),
            AvailableAmount = Money.FromNaira(0),
            UnusedAmount = Money.FromNaira(0),
            TotalReleasedAmount = Money.FromNaira(0),
            Status = status,
        };

        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();

        return wallet;
    }

    private async Task<WalletRule> SeedRuleAsync(long walletId)
    {
        var rule = new WalletRule
        {
            WalletId = walletId,
            Amount = Money.FromNaira(1_000m),
            Frequency = ReleaseFrequency.Daily,
            FrequencyConfig = "{}",
            StartDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow.AddDays(30),
        };

        await UnitOfWork.AddAsync(rule);
        await UnitOfWork.SaveChangesAsync();

        return rule;
    }

    private async Task<ScheduledRelease> SeedReleaseAsync(
        long walletId,
        long ruleId,
        ReleaseStatus status)
    {
        var release = new ScheduledRelease
        {
            WalletId = walletId,
            WalletRuleId = ruleId,
            Amount = Money.FromNaira(1_000m),
            ScheduledFor = DateTimeOffset.UtcNow.AddDays(1),
            Status = status,
        };

        await UnitOfWork.AddAsync(release);
        await UnitOfWork.SaveChangesAsync();

        return release;
    }

    // ---------------------------------------------------------
    // 1. Happy path - pause
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithActiveWallet_PausesWallet()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Active);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.True(result.IsSuccess);

        var updated = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(WalletStatus.Paused, updated.Status);
    }

    [Fact]
    public async Task Handle_WithActiveWallet_PausesScheduledReleases()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Active);
        var rule = await SeedRuleAsync(wallet.Id);
        var release1 = await SeedReleaseAsync(wallet.Id, rule.Id, ReleaseStatus.Scheduled);
        var release2 = await SeedReleaseAsync(wallet.Id, rule.Id, ReleaseStatus.Scheduled);
        var alreadyPaused = await SeedReleaseAsync(wallet.Id, rule.Id, ReleaseStatus.Paused);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id), default);

        var r1 = await Context.ScheduledReleases.AsNoTracking().FirstAsync(x => x.Id == release1.Id);
        var r2 = await Context.ScheduledReleases.AsNoTracking().FirstAsync(x => x.Id == release2.Id);
        var r3 = await Context.ScheduledReleases.AsNoTracking().FirstAsync(x => x.Id == alreadyPaused.Id);

        Assert.Equal(ReleaseStatus.Paused, r1.Status);
        Assert.Equal(ReleaseStatus.Paused, r2.Status);
        Assert.Equal(ReleaseStatus.Paused, r3.Status);
    }

    [Fact]
    public async Task Handle_WithActiveWallet_DoesNotTouchReleasedReleases()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Active);
        var rule = await SeedRuleAsync(wallet.Id);
        var released = await SeedReleaseAsync(wallet.Id, rule.Id, ReleaseStatus.Released);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id), default);

        var unchanged = await Context.ScheduledReleases
            .AsNoTracking()
            .FirstAsync(x => x.Id == released.Id);

        Assert.Equal(ReleaseStatus.Released, unchanged.Status);
    }

    [Fact]
    public async Task Handle_WithActiveWallet_ReturnsPausedStatusInResponse()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Active);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Contains(
        "paused",
        result.Message,
        StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------
    // 2. Happy path - resume
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithPausedWallet_ResumesWallet()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Paused);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.True(result.IsSuccess);

        var updated = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(WalletStatus.Active, updated.Status);
    }

    [Fact]
    public async Task Handle_WithPausedWallet_ResumesPausedReleases()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Paused);
        var rule = await SeedRuleAsync(wallet.Id);
        var paused1 = await SeedReleaseAsync(wallet.Id, rule.Id, ReleaseStatus.Paused);
        var paused2 = await SeedReleaseAsync(wallet.Id, rule.Id, ReleaseStatus.Paused);
        var stillScheduled = await SeedReleaseAsync(wallet.Id, rule.Id, ReleaseStatus.Scheduled);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id), default);

        var r1 = await Context.ScheduledReleases.AsNoTracking().FirstAsync(x => x.Id == paused1.Id);
        var r2 = await Context.ScheduledReleases.AsNoTracking().FirstAsync(x => x.Id == paused2.Id);
        var r3 = await Context.ScheduledReleases.AsNoTracking().FirstAsync(x => x.Id == stillScheduled.Id);

        Assert.Equal(ReleaseStatus.Scheduled, r1.Status);
        Assert.Equal(ReleaseStatus.Scheduled, r2.Status);
        Assert.Equal(ReleaseStatus.Scheduled, r3.Status);
    }

    [Fact]
    public async Task Handle_WithPausedWallet_ReturnsResumedStatusInResponse()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Paused);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Contains("resumed", result.Message);
    }

    // ---------------------------------------------------------
    // 3. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var command = new ToggleWalletStatusCommand.Command
        {
            UserPublicId = string.Empty,
            WalletId = 1,
        };

        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithUnknownWallet_ReturnsNotFound()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(999_999L), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithWalletBelongingToAnotherUser_ReturnsNotFound()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Active);

        var command = new ToggleWalletStatusCommand.Command
        {
            UserPublicId = "someone_else",
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
    public async Task Handle_WithBrokenWallet_ReturnsBadRequest()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Broken);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("broken", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithCompletedWallet_ReturnsBadRequest()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Completed);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("completed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WithClosedWallet_ReturnsBadRequest()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Closed);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("closed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------
    // 4. REGRESSION: notification failures must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillReturnsSuccess()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Active);

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

        Assert.Equal(WalletStatus.Paused, updated.Status);
    }

    [Fact]
    public async Task Handle_WhenNotificationThrows_ReleasesStillUpdated()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Active);
        var rule = await SeedRuleAsync(wallet.Id);
        var release = await SeedReleaseAsync(wallet.Id, rule.Id, ReleaseStatus.Scheduled);

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
        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.True(result.IsSuccess);

        var updated = await Context.ScheduledReleases
            .AsNoTracking()
            .FirstAsync(x => x.Id == release.Id);

        Assert.Equal(ReleaseStatus.Paused, updated.Status);
    }

    // ---------------------------------------------------------
    // 5. REGRESSION: notification content
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPausing_SendsPauseNotificationWithCorrectContent()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Active);
        var handler = CreateHandler();

        await handler.Handle(CreateCommand(wallet.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                "Rent Savings wallet paused",
                It.Is<string>(m => m.Contains("on hold")),
                $"/wallet/{wallet.Id}",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenResuming_SendsResumeNotificationWithCorrectContent()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Paused);
        var handler = CreateHandler();

        await handler.Handle(CreateCommand(wallet.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                "Rent Savings wallet resumed",
                It.Is<string>(m => m.Contains("active again")),
                $"/wallet/{wallet.Id}",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPausing_DoesNotSendEmail()
    {
        var wallet = await SeedWalletAsync(WalletStatus.Active);
        var handler = CreateHandler();

        await handler.Handle(CreateCommand(wallet.Id), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }
}