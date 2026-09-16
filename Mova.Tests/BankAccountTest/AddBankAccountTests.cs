using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.BanksAccount;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class AddBankAccountTests : BaseTest
{
    private const string UserPublicId = "user_addbank_test";
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";
    private const string AccountNumber = "0123456789";
    private const string BankCode = "058";
    private const string BankName = "GTBank";
    private const string VerifiedAccountName = "Lucky Starboy";

    private readonly Mock<IPaystackService> _paystack = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private AddBankAccount.Handler CreateHandler()
    {
        return new AddBankAccount.Handler(
            UnitOfWork,
            _paystack.Object,
            _notifications.Object,
            Mock.Of<ILogger<AddBankAccount.Handler>>());
    }

    private AddBankAccount.Command CreateCommand(
        string accountNumber = AccountNumber,
        string bankCode = BankCode,
        bool consent = true)
    {
        return new AddBankAccount.Command
        {
            UserPublicId = UserPublicId,
            Email = UserEmail,
            FirstName = UserFirstName,
            AccountNumber = accountNumber,
            BankCode = bankCode,
            Consent = consent,
        };
    }

    private async Task<Bank> SeedBankAsync(
        string code = BankCode,
        string name = BankName,
        bool isActive = true)
    {
        var bank = new Bank
        {
            Code = code,
            Name = name,
            Logo = "https://example.com/gtb.png",
            IsActive = isActive,
        };

        await UnitOfWork.AddAsync(bank);
        await UnitOfWork.SaveChangesAsync();

        return bank;
    }

    private void SetupPaystackVerifySucceeds(
        string accountNumber = AccountNumber,
        string accountName = VerifiedAccountName)
    {
        _paystack
            .Setup(x => x.ResolveBankAccountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolveBankAccountResponse
            {
                AccountNumber = accountNumber,
                AccountName = accountName,
            });
    }

    private void SetupPaystackVerifyFails()
    {
        _paystack
            .Setup(x => x.ResolveBankAccountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolveBankAccountResponse?)null);
    }

    private void SetupHappyPath()
    {
        SetupPaystackVerifySucceeds();
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsCreated()
    {
        await SeedBankAsync();
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Created, result.StatusCode);
        Assert.Equal("Bank account added successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_PersistsBankAccount()
    {
        await SeedBankAsync();
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);

        var saved = await Context.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == result.Data!.Id);

        Assert.NotNull(saved);
        Assert.Equal(AccountNumber, saved!.AccountNumber);
        Assert.Equal(VerifiedAccountName, saved.AccountName);
        Assert.Equal(BankCode, saved.BankCode);
        Assert.Equal(BankName, saved.BankName);
        Assert.Equal(BankAccountStatus.Active, saved.Status);
        Assert.True(saved.ConsentGiven);
        Assert.NotNull(saved.VerifiedAt);
    }

    [Fact]
    public async Task Handle_WithFirstAccount_MarksAsDefault()
    {
        await SeedBankAsync();
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.True(result.Data!.IsDefault);

        var saved = await Context.BankAccounts
            .AsNoTracking()
            .FirstAsync(x => x.Id == result.Data.Id);

        Assert.True(saved.IsDefault);
    }

    [Fact]
    public async Task Handle_WithExistingDefaultAccount_DoesNotMarkAsDefault()
    {
        await SeedBankAsync();
        SetupHappyPath();

        var existingDefault = new BankAccount
        {
            UserPublicId = UserPublicId,
            AccountNumber = "0987654321",
            AccountName = "Existing Account",
            BankCode = BankCode,
            BankName = BankName,
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
        await UnitOfWork.AddAsync(existingDefault);
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.False(result.Data!.IsDefault);
    }

    [Fact]
    public async Task Handle_WithValidRequest_QueuesInAppNotification()
    {
        await SeedBankAsync();
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.System,
                "Bank account added",
                It.Is<string>(m => m.Contains(VerifiedAccountName) && m.Contains(BankName)),
                "/bank",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_QueuesEmailNotification()
    {
        await SeedBankAsync();
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m => m.Contains(VerifiedAccountName) && m.Contains(BankName)),
                "Your bank account has been linked"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsPaystackVerifyOnce()
    {
        await SeedBankAsync();
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _paystack.Verify(
            x => x.ResolveBankAccountAsync(
                AccountNumber,
                BankCode,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 2. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyAccountNumber_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(accountNumber: "   "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Account number is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithShortAccountNumber_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(accountNumber: "12345"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid account number", result.Message);
    }

    [Fact]
    public async Task Handle_WithNonDigitAccountNumber_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(accountNumber: "01234567ab"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid account number", result.Message);
    }

    [Fact]
    public async Task Handle_WithEmptyBankCode_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(bankCode: "   "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Bank code is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithoutConsent_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(consent: false), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Consent is required", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownBankCode_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(bankCode: "999"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid bank.", result.Message);
    }

    [Fact]
    public async Task Handle_WithInactiveBank_ReturnsBadRequest()
    {
        await SeedBankAsync(isActive: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid bank.", result.Message);
    }

    [Fact]
    public async Task Handle_WithDuplicateAccount_ReturnsConflict()
    {
        await SeedBankAsync();
        SetupHappyPath();

        var existing = new BankAccount
        {
            UserPublicId = UserPublicId,
            AccountNumber = AccountNumber,
            AccountName = "Existing",
            BankCode = BankCode,
            BankName = BankName,
            BankImageUrl = "https://example.com/gtb.png",
            Status = BankAccountStatus.Active,
            IsDefault = false,
            ConsentGiven = true,
            ConsentGivenAt = DateTimeOffset.UtcNow,
            ConsentVersion = "v1",
            Currency = "NGN",
            VerifiedAt = DateTimeOffset.UtcNow,
            VerificationMessage = "Verified",
        };
        await UnitOfWork.AddAsync(existing);
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, result.StatusCode);
        Assert.Contains("already been added", result.Message);
    }

    [Fact]
    public async Task Handle_WhenPaystackVerifyFails_ReturnsBadRequest()
    {
        await SeedBankAsync();
        SetupPaystackVerifyFails();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Unable to verify", result.Message);

        var anySaved = await Context.BankAccounts.AnyAsync();
        Assert.False(anySaved);
    }

    // ---------------------------------------------------------
    // 3. Regression: notification failures must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillReturnsCreated()
    {
        await SeedBankAsync();
        SetupHappyPath();

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
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Created, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenEmailQueueThrows_StillReturnsCreated()
    {
        await SeedBankAsync();
        SetupHappyPath();

        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Hangfire unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WhenBothNotificationsThrow_StillReturnsCreated()
    {
        await SeedBankAsync();
        SetupHappyPath();

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
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);

        var saved = await Context.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserPublicId == UserPublicId);

        Assert.NotNull(saved);
    }

    [Fact]
    public async Task Handle_WhenInAppThrows_EmailIsStillAttempted()
    {
        await SeedBankAsync();
        SetupHappyPath();

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
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }
}