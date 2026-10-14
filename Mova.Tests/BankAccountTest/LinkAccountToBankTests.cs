using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class LinkAccountToBankTests : BaseTestWithPinChallenge
{
    private const string UserPublicId = "user_linkbank_test";
    private const string OtherUserPublicId = "someone_else";
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";

    private const string WalletName = "Rent Savings";
    private const string AccountName = "Lucky Starboy";
    private const string AccountNumber = "0123456789";
    private const string BankCode = "058";
    private const string BankName = "GTBank";
    private const string BankImageUrl = "https://example.com/gtb.png";

    private readonly Mock<INotificationQueue> _notifications = new();
    private readonly Mock<IIdentityService> _identityService = new();

    public LinkAccountToBankTests()
    {
        SetupPermissions();
    }

    private LinkAccountToBank.Handler CreateHandler()
    {
        return new LinkAccountToBank.Handler(
            UnitOfWork,
            _notifications.Object,
            _identityService.Object,
            PinChallenge.Object,
            Mock.Of<ILogger<LinkAccountToBank.Handler>>());
    }

    private LinkAccountToBank.Command CreateCommand(
        long walletId,
        long bankAccountId,
        string? userPublicId = null,
        string? email = null,
        string? firstName = null)
    {
        return new LinkAccountToBank.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            Email = email ?? UserEmail,
            FirstName = firstName ?? UserFirstName,
            WalletId = walletId,
            BankAccountId = bankAccountId,
        };
    }

    private void SetupPermissions(
        bool canPerformSensitiveOperations = true,
        string statusLabel = "Active",
        string statusDescription = "Your account is in good standing and fully operational.",
        string? restrictionReason = null)
    {
        _identityService
            .Setup(x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserPermissionsDto(
                UserPublicId,
                UserAccountStatus.Active,
                statusLabel,
                statusDescription,
                true,
                true,
                canPerformSensitiveOperations,
                true,
                true,
                restrictionReason,
                null,
                null,
                null));
    }

    private void SetupPermissionsNull()
    {
        _identityService
            .Setup(x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPermissionsDto?)null);
    }

    private async Task<Wallet> SeedWalletAsync(
        string userPublicId = UserPublicId,
        string name = WalletName,
        long? bankAccountId = null)
    {
        var category = new WalletCategory
        {
            Name = "Savings",
            Icon = "Bat"
        };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();

        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            Name = name,
            CategoryId = category.Id,
            BankAccountId = bankAccountId,
            Status = WalletStatus.Active,
            TargetAmount = Money.FromNaira(0),
            TotalReleasedAmount = Money.FromNaira(0),
            AvailableAmount = Money.FromNaira(0),
            TotalWithdrawnAmount = Money.FromNaira(0),
            LockedAmount = Money.FromNaira(0),
            FundedAmount = Money.FromNaira(0),
        };

        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();
        return wallet;
    }

    private async Task<BankAccount> SeedBankAccountAsync(
        string userPublicId = UserPublicId,
        string accountName = AccountName,
        string accountNumber = AccountNumber,
        string bankCode = BankCode,
        string bankName = BankName,
        string bankImageUrl = BankImageUrl,
        BankAccountStatus status = BankAccountStatus.Active,
        bool consentGiven = true)
    {
        var account = new BankAccount
        {
            UserPublicId = userPublicId,
            AccountNumber = accountNumber,
            AccountName = accountName,
            BankCode = bankCode,
            BankName = bankName,
            BankImageUrl = bankImageUrl,
            Status = status,
            IsDefault = false,
            ConsentGiven = consentGiven,
            ConsentGivenAt = DateTimeOffset.UtcNow,
            ConsentVersion = "v1",
            Currency = "NGN",
            VerifiedAt = DateTimeOffset.UtcNow,
            VerificationMessage = "Verified",
        };

        await UnitOfWork.AddAsync(account);
        await UnitOfWork.SaveChangesAsync();
        return account;
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsSuccess()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Bank account linked to wallet successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsBankAccountInResponse()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.NotNull(result.Data);
        Assert.Equal(bank.Id, result.Data!.Id);
        Assert.Equal(AccountName, result.Data.AccountName);
        Assert.Equal(AccountNumber, result.Data.AccountNumber);
        Assert.Equal(BankName, result.Data.BankName);
        Assert.Equal(BankImageUrl, result.Data.BankImageUrl);
        Assert.True(result.Data.Notification);
    }

    [Fact]
    public async Task Handle_WithValidRequest_LinksBankAccountToWallet()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        var updatedWallet = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(bank.Id, updatedWallet.BankAccountId);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SavesChanges()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ReplacesExistingBankAccount()
    {
        var oldBank = await SeedBankAccountAsync(
            accountNumber: "1111111111",
            accountName: "Old Account");

        var wallet = await SeedWalletAsync(bankAccountId: oldBank.Id);

        var newBank = await SeedBankAccountAsync(
            accountNumber: "2222222222",
            accountName: "New Account");

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, newBank.Id), default);

        var updatedWallet = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(newBank.Id, updatedWallet.BankAccountId);
    }

    // ---------------------------------------------------------
    // 2. Wallet not found
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithUnknownWallet_ReturnsNotFound()
    {
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(999_999, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Wallet not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithWalletBelongingToAnotherUser_ReturnsNotFound()
    {
        var wallet = await SeedWalletAsync(userPublicId: OtherUserPublicId);
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Wallet not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownWallet_DoesNotLinkAnything()
    {
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(999_999, bank.Id), default);

        var anyLinked = await Context.Wallets
            .AnyAsync(x => x.BankAccountId == bank.Id);

        Assert.False(anyLinked);
    }

    // ---------------------------------------------------------
    // 3. Bank account not found / invalid
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithUnknownBankAccount_ReturnsNotFound()
    {
        var wallet = await SeedWalletAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, 999_999),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Bank account not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithBankAccountBelongingToAnotherUser_ReturnsNotFound()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync(userPublicId: OtherUserPublicId);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Bank account not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithInactiveBankAccount_ReturnsNotFound()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync(
            status: BankAccountStatus.Suspended);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Bank account not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithNoConsentBankAccount_ReturnsNotFound()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync(consentGiven: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Bank account not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithBankAccountNotFound_DoesNotLinkAnything()
    {
        var wallet = await SeedWalletAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, 999_999), default);

        var reloaded = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Null(reloaded.BankAccountId);
    }

    [Fact]
    public async Task Handle_WithBankAccountNotFound_DoesNotSaveChanges()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, 999_999), default);

        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    // ---------------------------------------------------------
    // 4. Notifications
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_QueuesInAppNotification()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                "Bank account linked",
                It.Is<string>(m =>
                    m.Contains(BankName) &&
                    m.Contains(AccountNumber) &&
                    m.Contains(WalletName)),
                $"/wallet/{wallet.Id}",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_QueuesEmailNotification()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m =>
                    m.Contains(BankName) &&
                    m.Contains(AccountNumber) &&
                    m.Contains(WalletName)),
                It.Is<string>(s => s.Contains(WalletName))),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenWalletNotFound_DoesNotSendNotification()
    {
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(999_999, bank.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenBankNotFound_DoesNotSendNotification()
    {
        var wallet = await SeedWalletAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, 999_999), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }


    // ---------------------------------------------------------
    // 6. Permissions gating
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id, userPublicId: ""),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User identity is required.", result.Message);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_ReturnsInternalServerError()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissionsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Contains("couldn't verify", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotLinkWallet()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissionsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        var reloaded = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Null(reloaded.BankAccountId);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotSaveChanges()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissionsNull();

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotNotify()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissionsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_ReturnsLocked()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissions(
            canPerformSensitiveOperations: false,
            statusLabel: "Restricted",
            statusDescription: "Some features are temporarily limited. Existing wallets and releases continue normally.");

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
        Assert.Equal(
            "Some features are temporarily limited. Existing wallets and releases continue normally.",
            result.Message);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_UsesFallbackMessageWhenDescriptionEmpty()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissions(
            canPerformSensitiveOperations: false,
            statusDescription: string.Empty);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
        Assert.Contains("restricted", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_UsesFallbackMessageWhenDescriptionWhitespace()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissions(
            canPerformSensitiveOperations: false,
            statusDescription: "   ");

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
        Assert.Contains("restricted", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotLinkWallet()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        var reloaded = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Null(reloaded.BankAccountId);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotSaveChanges()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissions(canPerformSensitiveOperations: false);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotNotify()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(wallet.Id, bank.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotLookupWallet()
    {
        var bank = await SeedBankAccountAsync();
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(999_999, bank.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenPermissionsGranted_ProceedsToLinkWallet()
    {
        var wallet = await SeedWalletAsync();
        var bank = await SeedBankAccountAsync();
        SetupPermissions(canPerformSensitiveOperations: true);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(wallet.Id, bank.Id),
            default);

        Assert.True(result.IsSuccess);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        var reloaded = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == wallet.Id);

        Assert.Equal(bank.Id, reloaded.BankAccountId);
    }
}