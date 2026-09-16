using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class ChangePasswordCommandTests : BaseTest
{
    private const string UserPublicId = "user_changepass_test";

    private readonly Mock<IIdentityService> _identityService = new();

    private ChangePasswordCommand.Handler CreateHandler()
    {
        return new ChangePasswordCommand.Handler(
            UnitOfWork,
            _identityService.Object,
            Mock.Of<ILogger<ChangePasswordCommand.Handler>>());
    }

    private ChangePasswordCommand.Command CreateCommand(
        string oldPassword = "OldPass123!",
        string newPassword = "NewPass456!",
        string? confirmPassword = null)
    {
        return new ChangePasswordCommand.Command
        {
            UserPublicId = UserPublicId,
            OldPassword = oldPassword,
            NewPassword = newPassword,
            ConfirmPassword = confirmPassword ?? newPassword,
        };
    }

    private void SetupUserExists(long userId = 42, string publicId = UserPublicId)
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
                "user@mova.app",
                "08050000000",
                null,
                Money.FromNaira(0),
                string.Empty));
    }

    private void SetupChangePasswordSucceeds()
    {
        _identityService
            .Setup(x => x.ChangePasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync((true, string.Empty));
    }

    private void SetupChangePasswordFails(string message = "Current password is incorrect")
    {
        _identityService
            .Setup(x => x.ChangePasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
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
        SetupChangePasswordSucceeds();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Password changed successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsIdentityChangePasswordOnce()
    {
        SetupUserExists(userId: 42);
        SetupChangePasswordSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.ChangePasswordAsync(
                42L,
                "OldPass123!",
                "NewPass456!"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_RevokesAllActiveRefreshTokens()
    {
        SetupUserExists();
        SetupChangePasswordSucceeds();

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
        Assert.Equal("Password changed by user", t1.RevocationReason);
    }

    [Fact]
    public async Task Handle_WithValidRequest_DoesNotRevokeAlreadyRevokedTokens()
    {
        SetupUserExists();
        SetupChangePasswordSucceeds();

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
        SetupChangePasswordSucceeds();

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
        SetupChangePasswordSucceeds();

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
    public async Task Handle_WithMismatchedConfirmPassword_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var command = CreateCommand(
            newPassword: "NewPass456!",
            confirmPassword: "Different789!");

        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("do not match", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithNewPasswordSameAsOld_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var command = CreateCommand(
            oldPassword: "SamePass123!",
            newPassword: "SamePass123!");

        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("cannot be the same", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsBadRequest()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WhenIdentityChangePasswordFails_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupChangePasswordFails("Current password is incorrect");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Current password is incorrect", result.Message);
        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.RollbackCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Handle_WhenIdentityChangePasswordFails_DoesNotRevokeTokens()
    {
        SetupUserExists();
        SetupChangePasswordFails();

        var token = await SeedActiveRefreshTokenAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after = await Context.RefreshTokens
            .AsNoTracking()
            .FirstAsync(x => x.Id == token.Id);

        Assert.Null(after.RevokedAt);
    }

    // ---------------------------------------------------------
    // 3. Regression: rollback behaviour
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenChangePasswordThrows_RollsBackAndReturnsServerError()
    {
        SetupUserExists();

        _identityService
            .Setup(x => x.ChangePasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Identity provider unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.RollbackCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }
}