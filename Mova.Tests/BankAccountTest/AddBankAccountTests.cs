using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.BanksAccount;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class AddBankAccountTests : BaseTestWithPinChallenge
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
    private readonly Mock<IIdentityService> _identityService = new();

    public AddBankAccountTests()
    {
        SetupPermissions();
    }

    private AddBankAccount.Handler CreateHandler()
    {
        return new AddBankAccount.Handler(
            UnitOfWork,
            _paystack.Object,
            _notifications.Object,
            _identityService.Object,
            PinChallenge.Object,
            Mock.Of<ILogger<AddBankAccount.Handler>>());
    }

    private AddBankAccount.Command CreateCommand(
        string accountNumber = AccountNumber,
        string bankCode = BankCode,
        bool consent = true,
        string? userPublicId = null)
    {
        return new AddBankAccount.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            Email = UserEmail,
            FirstName = UserFirstName,
            AccountNumber = accountNumber,
            BankCode = bankCode,
            Consent = consent,
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
                "/dashboard/bank",
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


    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(userPublicId: ""), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
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
        SetupPermissionsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Contains("couldn't verify", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotCallPaystack()
    {
        SetupPermissionsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _paystack.Verify(
            x => x.ResolveBankAccountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotPersistAccount()
    {
        SetupPermissionsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var any = await Context.BankAccounts.AnyAsync();
        Assert.False(any);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotNotify()
    {
        SetupPermissionsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

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
        SetupPermissions(
            canPerformSensitiveOperations: false,
            statusLabel: "Restricted",
            statusDescription: "Some features are temporarily limited. Existing wallets and releases continue normally.");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Locked, result.StatusCode);
        Assert.Equal(
            "Some features are temporarily limited. Existing wallets and releases continue normally.",
            result.Message);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_UsesFallbackMessageWhenDescriptionEmpty()
    {
        SetupPermissions(
            canPerformSensitiveOperations: false,
            statusDescription: string.Empty);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Locked, result.StatusCode);
        Assert.Contains("restricted", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_UsesFallbackMessageWhenDescriptionWhitespace()
    {
        SetupPermissions(
            canPerformSensitiveOperations: false,
            statusDescription: "   ");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Locked, result.StatusCode);
        Assert.Contains("restricted", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotCallPaystack()
    {
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _paystack.Verify(
            x => x.ResolveBankAccountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotPersistAccount()
    {
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var any = await Context.BankAccounts.AnyAsync();
        Assert.False(any);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotNotify()
    {
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

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
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotValidateAccountNumber()
    {
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(accountNumber: "abc"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Locked, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenPermissionsGranted_ProceedsToPaystackVerify()
    {
        await SeedBankAsync();
        SetupHappyPath();
        SetupPermissions(canPerformSensitiveOperations: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _paystack.Verify(
            x => x.ResolveBankAccountAsync(
                AccountNumber,
                BankCode,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}