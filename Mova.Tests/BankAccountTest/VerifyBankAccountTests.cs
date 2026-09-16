using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.BanksAccount;
using Mova.Application.Interfaces.Payment;
using Mova.Domain.Entities;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class VerifyBankAccountTests : BaseTest
{
    private const string AccountNumber = "0123456789";
    private const string AccountName = "Lucky Starboy";
    private const string BankCode = "058";
    private const string BankName = "GTBank";

    private readonly Mock<IPaystackService> _paystack = new();

    private VerifyBankAccount.Handler CreateHandler()
    {
        return new VerifyBankAccount.Handler(
            _paystack.Object,
            UnitOfWork);
    }

    private VerifyBankAccount.Command CreateCommand(
        string accountNumber = AccountNumber,
        string bankCode = BankCode)
    {
        return new VerifyBankAccount.Command
        {
            AccountNumber = accountNumber,
            BankCode = bankCode,
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
                AccountNumber,     // trimmed
                BankCode,          // trimmed
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
}