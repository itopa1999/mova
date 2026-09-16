using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.BanksAccount;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class DeleteBankAccountTests : BaseTest
{
    private const string UserPublicId = "user_deletebank_test";
    private const string AccountNumber = "0123456789";
    private const string AccountName = "Lucky Starboy";
    private const string BankCode = "058";
    private const string BankName = "GTBank";

    private readonly Mock<INotificationQueue> _notifications = new();

    private DeleteBankAccount.Handler CreateHandler()
    {
        return new DeleteBankAccount.Handler(
            UnitOfWork,
            _notifications.Object,
            Mock.Of<ILogger<DeleteBankAccount.Handler>>());
    }

    private DeleteBankAccount.Command CreateCommand(
        long bankAccountId,
        string? userPublicId = null)
    {
        return new DeleteBankAccount.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            BankAccountId = bankAccountId,
        };
    }

    private async Task<BankAccount> SeedBankAccountAsync(
        string userPublicId = UserPublicId,
        string accountNumber = AccountNumber,
        string accountName = AccountName,
        string bankCode = BankCode,
        string bankName = BankName,
        BankAccountStatus status = BankAccountStatus.Active,
        bool isDefault = false)
    {
        var account = new BankAccount
        {
            UserPublicId = userPublicId,
            AccountNumber = accountNumber,
            AccountName = accountName,
            BankCode = bankCode,
            BankName = bankName,
            BankImageUrl = "https://example.com/gtb.png",
            Status = status,
            IsDefault = isDefault,
            ConsentGiven = true,
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
        var account = await SeedBankAccountAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(account.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Bank account deleted successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SoftDeletesAccount()
    {
        var account = await SeedBankAccountAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(account.Id), default);

        // Query filter hides IsDeleted = true rows, so use IgnoreQueryFilters
        var saved = await Context.BankAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == account.Id);

        Assert.NotNull(saved);
        Assert.True(saved!.IsDeleted);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SavesChanges()
    {
        var account = await SeedBankAccountAsync();

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(account.Id), default);

        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WithValidRequest_PersistsIsDeletedFlag()
    {
        var account = await SeedBankAccountAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(account.Id), default);

        var saved = await Context.BankAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(x => x.Id == account.Id);

        Assert.True(saved.IsDeleted);
    }

    // ---------------------------------------------------------
    // 2. Not found
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithUnknownBankAccount_ReturnsNotFound()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(999_999), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Bank account not found.", result.Message);
        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WithBankAccountBelongingToAnotherUser_ReturnsNotFound()
    {
        var account = await SeedBankAccountAsync(userPublicId: "someone_else");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(account.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Bank account not found.", result.Message);

        // Confirm the other user's account is untouched
        var stillThere = await Context.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == account.Id);

        Assert.NotNull(stillThere);
        Assert.False(stillThere!.IsDeleted);
    }

    [Fact]
    public async Task Handle_WithAlreadyDeletedBankAccount_ReturnsNotFound()
    {
        var account = await SeedBankAccountAsync();
        account.IsDeleted = true;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(account.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithBankAccountFromDifferentUser_DoesNotDeleteOtherUsers()
    {
        var myAccount = await SeedBankAccountAsync(userPublicId: UserPublicId);
        var otherAccount = await SeedBankAccountAsync(
            userPublicId: "someone_else",
            accountNumber: "9999999999");

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(myAccount.Id), default);

        var stillThere = await Context.BankAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == otherAccount.Id);

        Assert.NotNull(stillThere);
        Assert.False(stillThere!.IsDeleted);
    }

    // ---------------------------------------------------------
    // 3. Notifications
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_QueuesInAppNotification()
    {
        var account = await SeedBankAccountAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(account.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.System,
                "Bank account removed",
                It.Is<string>(m =>
                    m.Contains(AccountName) &&
                    m.Contains(BankName) &&
                    m.Contains("removed")),
                "/bank",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNotFound_DoesNotSendNotification()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(999_999), default);

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
    // 4. Regression: notification failures must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillReturnsSuccess()
    {
        var account = await SeedBankAccountAsync();

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
        var result = await handler.Handle(CreateCommand(account.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillDeletesAccount()
    {
        var account = await SeedBankAccountAsync();

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
        await handler.Handle(CreateCommand(account.Id), default);

        var saved = await Context.BankAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == account.Id);

        Assert.NotNull(saved);
        Assert.True(saved!.IsDeleted);
    }
}