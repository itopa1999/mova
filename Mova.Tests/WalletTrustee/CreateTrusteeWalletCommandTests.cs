using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.AccountWalletTrustee;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class CreateTrusteeWalletCommandTests : BaseTest
{
    private const string UserPublicId = "user_owner_test";
    private const string OtherUserPublicId = "user_other_test";
    private const string UserEmail = "owner@mova.app";
    private const string UserFirstName = "Lucky";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<INotificationQueue> _notifications = new();
    private readonly Mock<IPinChallenge> _pinChallenge = new();

    public CreateTrusteeWalletCommandTests()
    {
        SetupPermissions();
        SetupPinChallenge();
        SetupUserBalance();
    }

    private CreateTrusteeWalletCommand.Handler CreateHandler()
    {
        return new CreateTrusteeWalletCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            Mock.Of<ILogger<CreateTrusteeWalletCommand.Handler>>(),
            _notifications.Object,
            _pinChallenge.Object);
    }

    private CreateTrusteeWalletCommand.Command CreateCommand(
        string name = "Family Trust",
        decimal targetAmount = 50_000m,
        long categoryId = 1,
        long bankAccountId = 1,
        long? trusteeId = null,
        string email = UserEmail)
    {
        return new CreateTrusteeWalletCommand.Command
        {
            UserPublicId = UserPublicId,
            Email = email,
            FirstName = UserFirstName,
            Name = name,
            TargetAmount = targetAmount,
            CategoryId = categoryId,
            BankAccountId = bankAccountId,
            WalletTrusteeId = trusteeId,
        };
    }

    // ---------------------------------------------------------
    // Mock setups
    // ---------------------------------------------------------

    private void SetupPermissions(bool canCreateWallets = true)
    {
        _identityService
            .Setup(x => x.GetPermissionsAsync(UserPublicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserPermissionsDto(
                UserPublicId,
                UserAccountStatus.Active,
                "Active",
                "Good standing",
                true,
                canCreateWallets,
                true,
                true,
                true,
                null, null, null, null));
    }

    private void SetupPermissionsNull()
    {
        _identityService
            .Setup(x => x.GetPermissionsAsync(UserPublicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPermissionsDto?)null);
    }

    private void SetupPinChallenge(bool isValid = true)
    {
        _pinChallenge
            .Setup(x => x.ValidateAsync(
                UserPublicId,
                PinChallengeType.CreateWalletTrustee,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((isValid, isValid ? string.Empty : "Invalid PIN"));
    }

    private void SetupUserBalance(
        decimal balance = 100_000m,
        bool debitSucceeds = true)
    {
        _identityService
            .Setup(x => x.DebitBalanceAsync(
                UserPublicId,
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(debitSucceeds);

        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                1,
                UserPublicId,
                UserFirstName,
                null,
                "Starboy",
                UserEmail,
                "08050000000",
                null,
                Money.FromNaira(balance),
                string.Empty,
                true,
                true,
                true,
                false,
                string.Empty,
                DateTimeOffset.UtcNow));
    }

    // ---------------------------------------------------------
    // Seeds
    // ---------------------------------------------------------

    private async Task<WalletCategory> SeedCategoryAsync()
    {
        var category = new WalletCategory { Name = "Trust", Icon = "Shield" };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();
        return category;
    }

    private async Task<BankAccount> SeedBankAccountAsync(
        string userPublicId = UserPublicId,
        BankAccountStatus status = BankAccountStatus.Active)
    {
        var bank = new BankAccount
        {
            UserPublicId = userPublicId,
            AccountNumber = "0123456789",
            AccountName = "Lucky Starboy",
            BankCode = "058",
            BankName = "GTBank",
            BankImageUrl = "https://example.com/gtb.png",
            Status = status,
            Currency = "NGN",
        };
        await UnitOfWork.AddAsync(bank);
        await UnitOfWork.SaveChangesAsync();
        return bank;
    }

    private async Task<WalletTrustee> SeedTrusteeAsync(
        string ownerPublicId = UserPublicId,
        WalletTrusteeStatus status = WalletTrusteeStatus.Verified)
    {
        var trustee = new WalletTrustee
        {
            OwnerPublicId = ownerPublicId,
            Name = "Jane Doe",
            Email = "jane@mova.app",
            Status = status,
        };
        await UnitOfWork.AddAsync(trustee);
        await UnitOfWork.SaveChangesAsync();
        return trustee;
    }

    // =========================================================
    // 1. Validation & Permissions
    // =========================================================

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_ReturnsInternalServerError()
    {
        SetupPermissionsNull();
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenCannotCreateWallets_ReturnsLocked()
    {
        SetupPermissions(canCreateWallets: false);
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenPinChallengeFails_ReturnsBadRequest()
    {
        SetupPinChallenge(isValid: false);
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid PIN", result.Message);
    }

    [Fact]
    public async Task Handle_WithEmptyName_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(name: "   "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Wallet name is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithTargetAmountBelowMinimum_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(targetAmount: 1999m), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("₦2,000", result.Message);
    }

    [Fact]
    public async Task Handle_WithDuplicateName_ReturnsBadRequest()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();
        
        await UnitOfWork.AddAsync(new Wallet
        {
            UserPublicId = UserPublicId,
            Name = "Family Trust",
            CategoryId = cat.Id,
            TargetAmount = Money.FromNaira(10_000m),
            Type = WalletType.Trustee,
            Status = WalletStatus.Active,
        });
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(name: "Family Trust", categoryId: cat.Id, bankAccountId: bank.Id), 
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("already exists", result.Message);
    }

    [Fact]
    public async Task Handle_WithInvalidCategory_ReturnsBadRequest()
    {
        var bank = await SeedBankAccountAsync();
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(categoryId: 999, bankAccountId: bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("category does not exist", result.Message);
    }

    [Fact]
    public async Task Handle_WithBankAccountBelongingToAnotherUser_ReturnsBadRequest()
    {
        var cat = await SeedCategoryAsync();
        var otherBank = await SeedBankAccountAsync(userPublicId: OtherUserPublicId);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(categoryId: cat.Id, bankAccountId: otherBank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("does not belong to you", result.Message);
    }

    [Fact]
    public async Task Handle_WithInactiveBankAccount_ReturnsBadRequest()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync(status: BankAccountStatus.Suspended);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(categoryId: cat.Id, bankAccountId: bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("is not active", result.Message);
    }

    // =========================================================
    // 2. Trustee Validation
    // =========================================================

    [Fact]
    public async Task Handle_WithInvalidTrusteeId_ReturnsBadRequest()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: cat.Id, bankAccountId: bank.Id, trusteeId: 999), 
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("selected trustee does not exist", result.Message);
    }

    [Fact]
    public async Task Handle_WithTrusteeOwnedByAnotherUser_ReturnsBadRequest()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();
        var otherTrustee = await SeedTrusteeAsync(ownerPublicId: OtherUserPublicId);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: cat.Id, bankAccountId: bank.Id, trusteeId: otherTrustee.Id), 
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("does not belong to you", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnverifiedTrustee_ReturnsBadRequest()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();
        var pendingTrustee = await SeedTrusteeAsync(status: WalletTrusteeStatus.Pending);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: cat.Id, bankAccountId: bank.Id, trusteeId: pendingTrustee.Id), 
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("has not accepted their invitation", result.Message);
    }

    // =========================================================
    // 3. Financials
    // =========================================================

    [Fact]
    public async Task Handle_WhenInsufficientBalance_ReturnsBadRequest()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();
        
        SetupUserBalance(debitSucceeds: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: cat.Id, bankAccountId: bank.Id), 
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Insufficient account balance", result.Message);
        
        // Ensure transaction rolled back
        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.RollbackCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    // =========================================================
    // 4. Happy Paths
    // =========================================================

    [Fact]
    public async Task Handle_WithoutTrustee_CreatesWalletAndFundsIt()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(name: "My Trust", targetAmount: 50_000m, categoryId: cat.Id, bankAccountId: bank.Id), 
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);

        var wallet = await Context.Wallets.FirstAsync(x => x.Id == result.Data!.WalletId);
        Assert.Equal("My Trust", wallet.Name);
        Assert.Equal(WalletType.Trustee, wallet.Type);
        Assert.Equal(PayoutDestination.Trustee, wallet.PayoutDestination);
        Assert.Equal(50_000m, wallet.TargetAmount.ToDecimal());
        Assert.Equal(50_000m, wallet.LockedAmount.ToDecimal());
        Assert.Equal(50_000m, wallet.FundedAmount.ToDecimal());
        Assert.Null(wallet.WalletTrusteeId);

        // Verify deposit transaction
        var depositTxn = await Context.Transactions.FirstAsync(x => x.WalletId == wallet.Id && x.Type == TransactionType.Deposit);
        Assert.Equal(50_000m, depositTxn.Amount.ToDecimal());

        // Verify ledger
        var ledger = await Context.LedgerEntries.FirstAsync(x => x.WalletId == wallet.Id);
        Assert.Equal(50_000m, ledger.Amount.ToDecimal());
        Assert.True(ledger.IsCredit);

        // Verify fee transaction (if fees > 0)
        if (result.Data.TotalCharges > 0)
        {
            var feeTxn = await Context.Transactions.FirstAsync(x => x.WalletId == wallet.Id && x.Type == TransactionType.Fee);
            Assert.Equal(result.Data.TotalCharges, feeTxn.Amount.ToDecimal());
        }

        Assert.Equal(1, UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Handle_WithVerifiedTrustee_CreatesWalletAndLinksTrustee()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();
        var trustee = await SeedTrusteeAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: cat.Id, bankAccountId: bank.Id, trusteeId: trustee.Id), 
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(trustee.Id, result.Data!.WalletTrusteeId);

        var wallet = await Context.Wallets.FirstAsync(x => x.Id == result.Data.WalletId);
        Assert.Equal(trustee.Id, wallet.WalletTrusteeId);
    }

    // =========================================================
    // 5. Notifications
    // =========================================================

    [Fact]
    public async Task Handle_WithoutTrustee_SendsCorrectNotifications()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(name: "My Trust", targetAmount: 50_000m, categoryId: cat.Id, bankAccountId: bank.Id), 
            default);

        // In-App
        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                "My Trust trustee wallet created",
                It.Is<string>(m => m.Contains("50,000") && m.Contains("add the person you trust")),
                "/dashboard/wallets",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Email
        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m => m.Contains("50,000") && m.Contains("add the person you trust")),
                "Your My Trust trustee wallet is ready"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithTrustee_SendsCorrectNotifications()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();
        var trustee = await SeedTrusteeAsync();

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(name: "My Trust", targetAmount: 50_000m, categoryId: cat.Id, bankAccountId: bank.Id, trusteeId: trustee.Id), 
            default);

        // In-App
        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                "My Trust trustee wallet created",
                It.Is<string>(m => m.Contains("50,000") && m.Contains("already linked")),
                "/dashboard/wallets",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Email
        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m => m.Contains("50,000") && m.Contains("already set")),
                "Your My Trust trustee wallet is ready"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEmailIsMissing_SkipsEmailNotification()
    {
        var cat = await SeedCategoryAsync();
        var bank = await SeedBankAccountAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: cat.Id, bankAccountId: bank.Id, email: ""), 
            default);

        Assert.True(result.IsSuccess);

        // In-App is still sent
        _notifications.Verify(
            x => x.InAppNotificationAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Email is not
        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}