using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class VerifyPasswordTokenCommandTests : BaseTest
{
    private const string UserPublicId = "user_verify_token_test";
    private const long UserId = 42;

    private readonly Mock<IIdentityService> _identityService = new();

    private VerifyPasswordTokenCommand.Handler CreateHandler()
    {
        return new VerifyPasswordTokenCommand.Handler(
            UnitOfWork,
            _identityService.Object,
            Mock.Of<ILogger<VerifyPasswordTokenCommand.Handler>>());
    }

    private VerifyPasswordTokenCommand.Command CreateCommand(
        string? userPublicId = null,
        string token = "123456")
    {
        return new VerifyPasswordTokenCommand.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            Token = token
        };
    }

    private void SetupUserExists(
        long userId = UserId,
        string publicId = UserPublicId)
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

    private void SetupUserNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);
    }

    private async Task<OtpVerification> SeedOtpAsync(
        string publicId,
        string code,
        string purpose,
        bool isUsed = false,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? usedAt = null)
    {
        var otp = new OtpVerification
        {
            UserPublicId = publicId,
            OtpCode = code,
            Purpose = purpose,
            IsUsed = isUsed,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
            UsedAt = usedAt,
        };

        await UnitOfWork.AddAsync(otp);
        await UnitOfWork.SaveChangesAsync();
        return otp;
    }

    // ---------------------------------------------------------
    // 1. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var command = CreateCommand(userPublicId: "");

        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithEmptyToken_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var command = CreateCommand(token: "");

        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithTokenTooShort_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var command = CreateCommand(token: "12345");

        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithTokenTooLong_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var command = CreateCommand(token: "1234567");

        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    // ---------------------------------------------------------
    // 2. User lookup
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsBadRequest()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    // ---------------------------------------------------------
    // 3. OTP lookup — invalid / missing
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithNoOtp_ReturnsBadRequest()
    {
        SetupUserExists();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithWrongOtp_ReturnsBadRequest()
    {
        SetupUserExists();
        await SeedOtpAsync(UserPublicId, "654321", OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(token: "123456"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithExpiredOtp_ReturnsBadRequest()
    {
        SetupUserExists();
        await SeedOtpAsync(
            UserPublicId,
            "123456",
            OtpPurpose.PasswordReset,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithAlreadyUsedOtp_ReturnsBadRequest()
    {
        SetupUserExists();
        await SeedOtpAsync(
            UserPublicId,
            "123456",
            OtpPurpose.PasswordReset,
            isUsed: true,
            usedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_DoesNotAcceptOtpFromDifferentPurpose()
    {
        SetupUserExists();
        await SeedOtpAsync(
            UserPublicId,
            "123456",
            OtpPurpose.AccountVerification);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_DoesNotAcceptOtpFromDifferentUser()
    {
        SetupUserExists(publicId: UserPublicId);
        await SeedOtpAsync(
            "someone_else",
            "123456",
            OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_UsesMostRecentOtp_WhenMultipleExist()
    {
        SetupUserExists();
        await SeedOtpAsync(UserPublicId, "111111", OtpPurpose.PasswordReset);
        await SeedOtpAsync(UserPublicId, "222222", OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(token: "222222"),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    // ---------------------------------------------------------
    // 4. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidOtp_ReturnsSuccess()
    {
        SetupUserExists();
        await SeedOtpAsync(UserPublicId, "123456", OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.IsVerified);
        Assert.Equal(UserPublicId, result.Data.UserPublicId);
    }

    [Fact]
    public async Task Handle_WithValidOtp_MarksOtpAsUsed()
    {
        SetupUserExists();
        var otp = await SeedOtpAsync(
            UserPublicId,
            "123456",
            OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == otp.Id);

        Assert.True(after.IsUsed);
        Assert.NotNull(after.UsedAt);
    }

    [Fact]
    public async Task Handle_WithValidOtp_CommitsTransactionExactlyOnce()
    {
        SetupUserExists();
        await SeedOtpAsync(UserPublicId, "123456", OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.CommitCount);
        Assert.Equal(0, UnitOfWork.RollbackCount);
    }

    // ---------------------------------------------------------
    // 5. Regression: rollback behaviour
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenSaveChangesThrowsDbUpdateException_RollsBackAndReturnsConflict()
    {
        SetupUserExists();
        await SeedOtpAsync(UserPublicId, "123456", OtpPurpose.PasswordReset);

        // Use a UoW whose SaveChangesAsync always throws DbUpdateException.
        // Begin/Commit/Rollback, Query, Update all behave normally.
        var throwingUow = new ThrowingSaveChangesUnitOfWork(Context);

        var handler = new VerifyPasswordTokenCommand.Handler(
            throwingUow,
            _identityService.Object,
            Mock.Of<ILogger<VerifyPasswordTokenCommand.Handler>>());

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("An error occurred. Please try again.", result.Message);

        Assert.Equal(1, throwingUow.BeginCount);
        Assert.Equal(1, throwingUow.RollbackCount);
        Assert.Equal(0, throwingUow.CommitCount);
    }

    [Fact]
    public async Task Handle_WhenSaveChangesThrowsGenericException_RollsBackAndReturnsInternalServerError()
    {
        SetupUserExists();
        await SeedOtpAsync(UserPublicId, "123456", OtpPurpose.PasswordReset);

        var throwingUow = new ThrowingGenericExceptionUnitOfWork(Context);

        var handler = new VerifyPasswordTokenCommand.Handler(
            throwingUow,
            _identityService.Object,
            Mock.Of<ILogger<VerifyPasswordTokenCommand.Handler>>());

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal(
            "An error occurred while verifying your OTP. Please try again later.",
            result.Message);

        Assert.Equal(1, throwingUow.BeginCount);
        Assert.Equal(1, throwingUow.RollbackCount);
        Assert.Equal(0, throwingUow.CommitCount);
    }
}