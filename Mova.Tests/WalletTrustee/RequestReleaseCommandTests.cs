using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Mova.Application.BBL.Commands.AccountWalletTrustee;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class RequestReleaseCommandTests : BaseTest
{
    private const string OwnerPublicId = "owner_123";
    private const string OwnerFirstName = "Lucky";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<INotificationQueue> _notifications = new();
    private readonly Mock<IPinChallenge> _pinChallenge = new();
    private readonly Mock<IOptions<TrusteeOptions>> _optionsMock = new();

    public RequestReleaseCommandTests()
    {
        _pinChallenge
            .Setup(x => x.ValidateAsync(OwnerPublicId, PinChallengeType.RequestRelease, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));

        _optionsMock.Setup(x => x.Value).Returns(new TrusteeOptions
        {
            ReleaseRequestExpiryHours = 48,
            ReleaseConsentBaseUrl = "https://mova.com/release"
        });
    }

    private RequestReleaseCommand.Handler CreateHandler()
    {
        return new RequestReleaseCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            _notifications.Object,
            _pinChallenge.Object,
            _optionsMock.Object,
            Mock.Of<ILogger<RequestReleaseCommand.Handler>>());
    }

    private RequestReleaseCommand.Command CreateCommand(long walletId, decimal amount = 100m)
    {
        return new RequestReleaseCommand.Command
        {
            UserPublicId = OwnerPublicId,
            FirstName = OwnerFirstName,
            WalletId = walletId,
            Amount = amount,
            UserNote = "Need this for rent"
        };
    }

    private async Task<Wallet> SeedWalletAsync(
        WalletTrusteeStatus trusteeStatus = WalletTrusteeStatus.Verified,
        decimal lockedAmount = 500m,
        WalletStatus walletStatus = WalletStatus.Active)
    {
        var cat = new WalletCategory { Name = "Test", Icon = "Test" };
        await UnitOfWork.AddAsync(cat);

        var trustee = new WalletTrustee
        {
            OwnerPublicId = OwnerPublicId,
            Name = "Jane Doe",
            Email = "jane@test.com",
            Status = trusteeStatus
        };
        await UnitOfWork.AddAsync(trustee);

        var bank = new BankAccount
        {
            UserPublicId = OwnerPublicId,
            AccountNumber = "0123456789",
            BankName = "GTBank",
            Status = BankAccountStatus.Active
        };
        await UnitOfWork.AddAsync(bank);
        await UnitOfWork.SaveChangesAsync();

        var wallet = new Wallet
        {
            UserPublicId = OwnerPublicId,
            Name = "Rent Wallet",
            CategoryId = cat.Id,
            WalletTrusteeId = trustee.Id,
            BankAccountId = bank.Id,
            Type = WalletType.Trustee,
            Status = walletStatus,
            LockedAmount = Money.FromNaira(lockedAmount),
            TargetAmount = Money.FromNaira(1000m),
            PayoutDestination = PayoutDestination.Bank
        };
        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();
        return wallet;
    }

    [Fact]
    public async Task Handle_WhenPinFails_ReturnsBadRequest()
    {
        _pinChallenge
            .Setup(x => x.ValidateAsync(OwnerPublicId, PinChallengeType.RequestRelease, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Invalid PIN"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(1), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenAmountExceedsLocked_ReturnsBadRequest()
    {
        var wallet = await SeedWalletAsync(lockedAmount: 50m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(wallet.Id, amount: 100m), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Amount exceeds locked balance", result.Message);
    }

    [Fact]
    public async Task Handle_WhenTrusteeNotVerified_ReturnsBadRequest()
    {
        var wallet = await SeedWalletAsync(trusteeStatus: WalletTrusteeStatus.Pending);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(wallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("not verified their email yet", result.Message);
    }

    [Fact]
    public async Task Handle_WhenValid_CreatesRequestAndDeductsFunds()
    {
        var wallet = await SeedWalletAsync(lockedAmount: 500m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(wallet.Id, amount: 200m), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);

        // Verify Request created
        var request = await Context.ReleaseRequests.SingleAsync();
        Assert.Equal(200m, request.Amount.ToDecimal());
        Assert.Equal(ReleaseRequestStatus.Pending, request.Status);

        // Verify Wallet deducted (500 - 200 = 300)
        var updatedWallet = await Context.Wallets.FindAsync(wallet.Id);
        Assert.Equal(300m, updatedWallet!.LockedAmount.ToDecimal());

        // Verify Email
        _notifications.Verify(x => x.QueueNotificationEmail(
            "Jane Doe",
            "jane@test.com",
            It.Is<string>(m => m.Contains("200") && m.Contains("https://mova.com/release/")),
            "Release request from Lucky"), Times.Once);
    }
}