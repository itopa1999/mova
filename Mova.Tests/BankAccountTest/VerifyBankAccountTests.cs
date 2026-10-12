using System.Net;
using Moq;
using Mova.Application.BBL.Commands.BanksAccount;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Mova.Domain.Enums;

namespace Mova.Tests.Handlers;

public sealed class VerifyBankAccountTests : BaseTest
{
    private const string UserPublicId = "user_verifybank_test";
    private const string AccountNumber = "0123456789";
    private const string AccountName = "Lucky Starboy";
    private const string BankCode = "058";
    private const string BankName = "GTBank";

    private readonly Mock<IPaystackService> _paystack = new();
    private readonly Mock<IIdentityService> _identityService = new();

    public VerifyBankAccountTests()
    {
        SetupPermissions();
    }

    private VerifyBankAccount.Handler CreateHandler()
    {
        return new VerifyBankAccount.Handler(
            _paystack.Object,
            UnitOfWork,
            _identityService.Object);
    }

    private VerifyBankAccount.Command CreateCommand(
        string accountNumber = AccountNumber,
        string bankCode = BankCode,
        string? userPublicId = null)
    {
        return new VerifyBankAccount.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            AccountNumber = accountNumber,
            BankCode = bankCode,
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

    private void SetupPaystackResolve(
        string accountNumber = AccountNumber,
        string accountName = AccountName)
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

    private void SetupPaystackReturnsNull()
    {
        _paystack
            .Setup(x => x.ResolveBankAccountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolveBankAccountResponse?)null);
    }

    // ---------------------------------------------------------
    // 1. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(userPublicId: ""),
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
    public async Task Handle_WithEmptyAccountNumber_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(accountNumber: "   "),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Account number is required.", result.Message);
        _paystack.Verify(
            x => x.ResolveBankAccountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithEmptyBankCode_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(bankCode: "   "),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Bank code is required.", result.Message);
        _paystack.Verify(
            x => x.ResolveBankAccountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 2. Bank lookup
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithUnknownBankCode_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(bankCode: "999"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid bank selected.", result.Message);

        _paystack.Verify(
            x => x.ResolveBankAccountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithInactiveBank_ReturnsBadRequest()
    {
        await SeedBankAsync(isActive: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid bank selected.", result.Message);
    }

    // ---------------------------------------------------------
    // 3. Paystack verification
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPaystackReturnsNull_ReturnsBadRequest()
    {
        await SeedBankAsync();
        SetupPaystackReturnsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Unable to verify bank account.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsPaystackOnce()
    {
        await SeedBankAsync();
        SetupPaystackResolve();

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
    // 4. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsSuccess()
    {
        await SeedBankAsync();
        SetupPaystackResolve();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Verified successfully", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsResolvedAccountDetails()
    {
        await SeedBankAsync();
        SetupPaystackResolve();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.Equal(AccountNumber, result.Data!.AccountNumber);
        Assert.Equal(AccountName, result.Data.AccountName);
        Assert.Equal(BankName, result.Data.BankInstitution);
        Assert.Equal(BankCode, result.Data.BankCode);
    }

    [Fact]
    public async Task Handle_WithValidRequest_UsesResolvedAccountName_NotRequest()
    {
        await SeedBankAsync();
        SetupPaystackResolve(accountName: "RESOLVED NAME FROM PAYSTACK");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.Equal("RESOLVED NAME FROM PAYSTACK", result.Data!.AccountName);
    }

    [Fact]
    public async Task Handle_WithValidRequest_UsesBankNameFromDatabase()
    {
        await SeedBankAsync(name: "Custom Bank Name");
        SetupPaystackResolve();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.Equal("Custom Bank Name", result.Data!.BankInstitution);
    }

    // ---------------------------------------------------------
    // 5. Trimming behaviour
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_TrimsAccountNumberAndBankCode()
    {
        await SeedBankAsync();
        SetupPaystackResolve();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(
                accountNumber: $"  {AccountNumber}  ",
                bankCode: $"  {BankCode}  "),
            default);

        Assert.True(result.IsSuccess);

        _paystack.Verify(
            x => x.ResolveBankAccountAsync(
                AccountNumber,
                BankCode,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 6. No DB writes
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_DoesNotWriteToDatabase()
    {
        await SeedBankAsync();
        SetupPaystackResolve();

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(0, UnitOfWork.SaveChangesCount);
        Assert.Empty(UnitOfWork.AddedEntities);
        Assert.Empty(UnitOfWork.UpdatedEntities);
        Assert.Empty(UnitOfWork.RemovedEntities);
    }

    // ---------------------------------------------------------
    // 7. Permissions gating
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_ReturnsInternalServerError()
    {
        SetupPermissionsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
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
    public async Task Handle_WhenSensitiveOperationsBlocked_ReturnsLocked()
    {
        SetupPermissions(
            canPerformSensitiveOperations: false,
            statusLabel: "Restricted",
            statusDescription: "Some features are temporarily limited. Existing wallets and releases continue normally.");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
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
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
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
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
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
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotQueryBank()
    {
        await SeedBankAsync();
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        // The bank is seeded but shouldn't be touched. Best we can assert
        // without a bank repository mock is that Paystack was never called.
        _paystack.Verify(
            x => x.ResolveBankAccountAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPermissionsGranted_ProceedsToPaystack()
    {
        await SeedBankAsync();
        SetupPaystackResolve();
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

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotLeakAccountName()
    {
        await SeedBankAsync();
        SetupPaystackResolve(accountName: "SHOULD NOT BE RETURNED");
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
    }
}