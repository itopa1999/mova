using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.AccountWalletTrustee;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class ApproveReleaseRequestCommandTests : BaseTest
{
    private const string OwnerPublicId = "user_owner_test";
    private const string OwnerEmail = "owner@mova.app";
    private const string OwnerFirstName = "Lucky";
    
    private readonly Mock<INotificationQueue> _notifications = new();
    private readonly Mock<IIdentityService> _identityService = new();

    private ApproveReleaseRequestCommand.Handler CreateHandler()
    {
        return new ApproveReleaseRequestCommand.Handler(
            UnitOfWork,
            _identityService.Object,
            _notifications.Object,
            Mock.Of<ILogger<ApproveReleaseRequestCommand.Handler>>());
    }

    private ApproveReleaseRequestCommand.Command CreateCommand(
        string token,
        string? ipAddress = "192.168.1.1")
    {
        return new ApproveReleaseRequestCommand.Command
        {
            Token = token,
            IpAddress = ipAddress,
        };
    }

    private void SetupOwnerExists(
        string email = OwnerEmail, 
        string firstName = OwnerFirstName)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                OwnerPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                1,
                OwnerPublicId,
                firstName,
                null,
                "Starboy",
                email,
                "08050000000",
                null,
                Money.FromNaira(0),
                string.Empty,
                true,
                true,
                true,
                false,
                string.Empty,
                DateTimeOffset.UtcNow));
    }

    private void SetupOwnerNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                OwnerPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);
    }

    private async Task<(
        WalletCategory category, 
        BankAccount bank, 
        WalletTrustee trustee, 
        Wallet wallet, 
        ReleaseRequest request)> 
        SeedRequestPrerequisitesAsync(
            string token,
            ReleaseRequestStatus status = ReleaseRequestStatus.Pending,
            DateTimeOffset? expiresAt = null,
            WalletStatus walletStatus = WalletStatus.Active,
            bool hasBankAccount = true)
    {
        var category = new WalletCategory { Name = "Rent", Icon = "Wallet" };
        await UnitOfWork.AddAsync(category);
        
        var bank = new BankAccount
        {
            UserPublicId = OwnerPublicId,
            AccountNumber = "0123456789",
            AccountName = "Lucky Starboy",
            BankCode = "058",
            BankName = "GTBank",
            BankImageUrl = "https://example.com/gtb.png",
            Status = BankAccountStatus.Active,
            Currency = "NGN",
        };
        await UnitOfWork.AddAsync(bank);

        var trustee = new WalletTrustee
        {
            OwnerPublicId = OwnerPublicId,
            Name = "Jane Doe",
            Email = "trustee@mova.app",
            Status = WalletTrusteeStatus.Verified,
        };
        await UnitOfWork.AddAsync(trustee);

        await UnitOfWork.SaveChangesAsync();

        var wallet = new Wallet
        {
            UserPublicId = OwnerPublicId,
            Name = "Rent Savings",
            CategoryId = category.Id,
            BankAccountId = hasBankAccount ? bank.Id : null,
            WalletTrusteeId = trustee.Id,
            TargetAmount = Money.FromNaira(100_000m),
            LockedAmount = Money.FromNaira(0m),
            TotalReleasedAmount = Money.FromNaira(0m),
            Status = walletStatus,
        };
        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var request = new ReleaseRequest
        {
            WalletId = wallet.Id,
            RequestedByUserPublicId = OwnerPublicId,
            Amount = Money.FromNaira(5_000m),
            Status = status,
            ApprovalTokenHash = tokenHash,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(24),
        };
        await UnitOfWork.AddAsync(request);
        await UnitOfWork.SaveChangesAsync();

        return (category, bank, trustee, wallet, request);
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ApprovesRequestAndCreatesPayout()
    {
        var token = "valid-token-12345678901234567890";
        var (_, bank, trustee, wallet, request) = await SeedRequestPrerequisitesAsync(token);
        SetupOwnerExists();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // Verify Release Request status
        var updatedRequest = await Context.ReleaseRequests.AsNoTracking().FirstAsync(x => x.Id == request.Id);
        Assert.Equal(ReleaseRequestStatus.Approved, updatedRequest.Status);
        Assert.NotNull(updatedRequest.DecidedAt);
        Assert.Equal(trustee.Id, updatedRequest.DecidedByTrusteeId);
        Assert.Null(updatedRequest.ApprovalTokenHash);

        // Verify Wallet TotalReleasedAmount
        var updatedWallet = await Context.Wallets.AsNoTracking().FirstAsync(x => x.Id == wallet.Id);
        Assert.Equal(5_000m, updatedWallet.TotalReleasedAmount.ToDecimal());

        // Verify Payout
        var payout = await Context.Payouts.AsNoTracking().FirstAsync(x => x.WalletId == wallet.Id);
        Assert.Equal(5_000m, payout.Amount.ToDecimal());
        Assert.Equal(PayoutStatus.Pending, payout.Status);
        Assert.Equal(PayoutDestination.Bank, payout.Destination);
        Assert.Equal(bank.Id, payout.BankAccountId);

        // Verify Transaction & Ledger
        var transaction = await Context.Transactions.AsNoTracking().FirstAsync(x => x.WalletId == wallet.Id);
        Assert.Equal(TransactionType.Withdrawal, transaction.Type);
        Assert.Equal(TransactionStatus.Completed, transaction.Status);

        var ledger = await Context.LedgerEntries.AsNoTracking().FirstAsync(x => x.TransactionId == transaction.Id);
        Assert.Equal(5_000m, ledger.Amount.ToDecimal());
        Assert.False(ledger.IsCredit);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SendsNotificationsToOwner()
    {
        var token = "valid-token-12345678901234567890";
        await SeedRequestPrerequisitesAsync(token);
        SetupOwnerExists();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(token), default);

        // Verify In-App Notification
        _notifications.Verify(
            x => x.InAppNotificationAsync(
                OwnerPublicId,
                NotificationType.Wallet,
                "Release approved",
                It.Is<string>(m => m.Contains("5,000") && m.Contains("GTBank")),
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify Email Notification
        _notifications.Verify(
            x => x.QueueNotificationEmail(
                OwnerFirstName,
                OwnerEmail,
                It.Is<string>(m => m.Contains("5,000") && m.Contains("GTBank")),
                "Release approved"),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 2. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithInvalidToken_ReturnsGone()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand("wrong-token-wrong-token-wrong-token"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
        Assert.Equal("This request has already been decided or has expired.", result.Message);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WhenRequestAlreadyDecided_ReturnsGone()
    {
        var token = "valid-token-12345678901234567890";
        await SeedRequestPrerequisitesAsync(token, status: ReleaseRequestStatus.Approved);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
        Assert.Equal("This request has already been decided or has expired.", result.Message);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WhenRequestExpired_ReturnsGone()
    {
        var token = "valid-token-12345678901234567890";
        await SeedRequestPrerequisitesAsync(token, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
        Assert.Equal("This request has already been decided or has expired.", result.Message);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WhenWalletIsNotActive_ReturnsBadRequest()
    {
        var token = "valid-token-12345678901234567890";
        await SeedRequestPrerequisitesAsync(token, walletStatus: WalletStatus.Paused);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("This wallet is no longer active.", result.Message);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WhenWalletHasNoBankAccount_ReturnsInternalServerError()
    {
        var token = "valid-token-12345678901234567890";
        await SeedRequestPrerequisitesAsync(token, hasBankAccount: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal("Wallet has no destination bank account.", result.Message);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    // ---------------------------------------------------------
    // 3. Edge cases
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenOwnerNotFound_SkipsEmailButStillSucceeds()
    {
        var token = "valid-token-12345678901234567890";
        await SeedRequestPrerequisitesAsync(token);
        SetupOwnerNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.True(result.IsSuccess);

        // In-app still goes
        _notifications.Verify(
            x => x.InAppNotificationAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string>(), 
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Email does NOT
        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOwnerHasNoEmail_SkipsEmailButStillSucceeds()
    {
        var token = "valid-token-12345678901234567890";
        await SeedRequestPrerequisitesAsync(token);
        SetupOwnerExists(email: string.Empty);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.True(result.IsSuccess);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}