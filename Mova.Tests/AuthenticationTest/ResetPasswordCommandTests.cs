using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class ResetPasswordCommandTests : BaseTest
{
    private const string UserPublicId = "user_resetpass_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string NewPassword = "NewPass123!";

    private readonly Mock<IIdentityService> _identityService = new();

    private ResetPasswordCommand.Handler CreateHandler()
    {
        return new ResetPasswordCommand.Handler(
            UnitOfWork,
            _identityService.Object,
            Mock.Of<ILogger<ResetPasswordCommand.Handler>>());
    }

    private ResetPasswordCommand.Command CreateCommand(
        string publicId = UserPublicId,
        string password = NewPassword)
    {
        return new ResetPasswordCommand.Command
        {
            UserPublicId = publicId,
            NewPassword = password,
        };
    }

    private void SetupUserExists(
        long userId = UserId,
        string publicId = UserPublicId,
        string? email = UserEmail)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                userId,
                publicId,
                "Lucky",
                null,
                "Starboy",
                email,
                "08050000000",
                null,
                Money.FromNaira(0),
                string.Empty));
    }

    private void SetupUserNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);
    }

    private void SetupResetSucceeds()
    {
        _identityService
            .Setup(x => x.ResetPasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>()))
            .ReturnsAsync((true, string.Empty));
    }

    private void SetupResetFails(string message = "Password does not meet requirements")
    {
        _identityService
            .Setup(x => x.ResetPasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>()))
            .ReturnsAsync((false, message));
    }

    private async Task<RefreshToken> SeedActiveRefreshTokenAsync(
        string publicId = UserPublicId)
    {
        var token = new RefreshToken
        {
            UserPublicId = publicId,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow,
            RevokedAt = null,
        };

        await UnitOfWork.AddAsync(token);
        await UnitOfWork.SaveChangesAsync();

        return token;
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsSuccess()
    {
        SetupUserExists();
        SetupResetSucceeds();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Password reset successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsResetOnce()
    {
        SetupUserExists(userId: 42);
        SetupResetSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.ResetPasswordAsync(42L, NewPassword),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_RevokesAllActiveRefreshTokens()
    {
        SetupUserExists();
        SetupResetSucceeds();

        var token1 = await SeedActiveRefreshTokenAsync();
        var token2 = await SeedActiveRefreshTokenAsync();
        var token3 = await SeedActiveRefreshTokenAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var t1 = await Context.RefreshTokens.AsNoTracking().FirstAsync(x => x.Id == token1.Id);
        var t2 = await Context.RefreshTokens.AsNoTracking().FirstAsync(x => x.Id == token2.Id);
        var t3 = await Context.RefreshTokens.AsNoTracking().FirstAsync(x => x.Id == token3.Id);

        Assert.NotNull(t1.RevokedAt);
        Assert.NotNull(t2.RevokedAt);
        Assert.NotNull(t3.RevokedAt);
        Assert.Equal("Password reset", t1.RevocationReason);
    }

    [Fact]
    public async Task Handle_WithValidRequest_DoesNotRevokeAlreadyRevokedTokens()
    {
        SetupUserExists();
        SetupResetSucceeds();

        var alreadyRevoked = new RefreshToken
        {
            UserPublicId = UserPublicId,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAt = DateTimeOffset.UtcNow.AddHours(-2),
            RevocationReason = "Old revoke",
        };
        await UnitOfWork.AddAsync(alreadyRevoked);
        await UnitOfWork.SaveChangesAsync();

        var originalRevokedAt = alreadyRevoked.RevokedAt;
        var originalReason = alreadyRevoked.RevocationReason;

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after = await Context.RefreshTokens
            .AsNoTracking()
            .FirstAsync(x => x.Id == alreadyRevoked.Id);

        Assert.Equal(originalRevokedAt, after.RevokedAt);
        Assert.Equal(originalReason, after.RevocationReason);
    }

    [Fact]
    public async Task Handle_WithValidRequest_DoesNotRevokeOtherUsersTokens()
    {
        SetupUserExists();
        SetupResetSucceeds();

        var otherUserToken = new RefreshToken
        {
            UserPublicId = "someone_else",
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow,
            RevokedAt = null,
        };
        await UnitOfWork.AddAsync(otherUserToken);
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after = await Context.RefreshTokens
            .AsNoTracking()
            .FirstAsync(x => x.Id == otherUserToken.Id);

        Assert.Null(after.RevokedAt);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CommitsTransactionExactlyOnce()
    {
        SetupUserExists();
        SetupResetSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.CommitCount);
        Assert.Equal(0, UnitOfWork.RollbackCount);
    }

    // ---------------------------------------------------------
    // 2. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(publicId: "   "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithEmptyPassword_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(password: "   "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithPasswordTooShort_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(password: "short"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("8 characters", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsBadRequest()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WhenResetFails_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupResetFails("Password too weak");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Password too weak", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    // ---------------------------------------------------------
    // 3. Regression: reset failure must not revoke tokens
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenResetFails_DoesNotRevokeTokens()
    {
        SetupUserExists();
        SetupResetFails();

        var token = await SeedActiveRefreshTokenAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after = await Context.RefreshTokens
            .AsNoTracking()
            .FirstAsync(x => x.Id == token.Id);

        Assert.Null(after.RevokedAt);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_DoesNotRevokeTokens()
    {
        SetupUserNotFound();

        var token = await SeedActiveRefreshTokenAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after = await Context.RefreshTokens
            .AsNoTracking()
            .FirstAsync(x => x.Id == token.Id);

        Assert.Null(after.RevokedAt);
    }

    // ---------------------------------------------------------
    // 4. Regression: rollback behaviour
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenSaveChangesThrows_RollsBackAndReturnsConflict()
    {
        SetupUserExists();
        SetupResetSucceeds();

        await SeedActiveRefreshTokenAsync();

        var failingUow = new Mock<Mova.Application.Interfaces.Persistence.IUnitOfWork>();

        failingUow
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        failingUow
            .Setup(x => x.Query<RefreshToken>())
            .Returns(UnitOfWork.Query<RefreshToken>());

        failingUow
            .Setup(x => x.Update(It.IsAny<RefreshToken>()))
            .Callback<RefreshToken>(t => UnitOfWork.Update(t));

        failingUow
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated failure"));

        failingUow
            .Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ResetPasswordCommand.Handler(
            failingUow.Object,
            _identityService.Object,
            Mock.Of<ILogger<ResetPasswordCommand.Handler>>());

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, result.StatusCode);
        failingUow.Verify(
            x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}