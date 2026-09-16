using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class ForgotPasswordCommandTests : BaseTest
{
    private const string UserPublicId = "user_forgotpass_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserPhone = "08050000000";
    private const string GeneratedOtp = "123456";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IOtpService> _otpService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private ForgotPasswordCommand.Handler CreateHandler()
    {
        return new ForgotPasswordCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            _otpService.Object,
            _notifications.Object,
            Mock.Of<ILogger<ForgotPasswordCommand.Handler>>());
    }

    private ForgotPasswordCommand.Command CreateCommand(string? email = UserEmail)
    {
        return new ForgotPasswordCommand.Command
        {
            Email = email,
        };
    }

    private void SetupUserExists(
        long userId = UserId,
        string publicId = UserPublicId,
        string? email = UserEmail,
        string? phone = UserPhone)
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
                phone,
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

    private void SetupAccountVerified(bool verified = true)
    {
        _identityService
            .Setup(x => x.IsAccountVerifiedAsync(It.IsAny<long>()))
            .ReturnsAsync(verified);
    }

    private void SetupOtpGeneration(string otp = GeneratedOtp)
    {
        _otpService
            .Setup(x => x.GenerateOtp())
            .Returns(otp);
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithVerifiedUser_SavesOtpRecord()
    {
        SetupUserExists();
        SetupAccountVerified();
        SetupOtpGeneration();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);

        var otp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserPublicId == UserPublicId);

        Assert.NotNull(otp);
        Assert.Equal(GeneratedOtp, otp!.OtpCode);
        Assert.Equal(OtpPurpose.PasswordReset, otp.Purpose);
        Assert.False(otp.IsUsed);
        Assert.True(otp.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithVerifiedUser_QueuesOtpDelivery()
    {
        SetupUserExists();
        SetupAccountVerified();
        SetupOtpGeneration();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueForgotPasswordOtp(
                UserEmail,
                UserPhone,
                GeneratedOtp),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithVerifiedUser_ReturnsSuccessWithUserPublicId()
    {
        SetupUserExists();
        SetupAccountVerified();
        SetupOtpGeneration();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
    }

    [Fact]
    public async Task Handle_WithVerifiedUser_CommitsTransactionExactlyOnce()
    {
        SetupUserExists();
        SetupAccountVerified();
        SetupOtpGeneration();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.CommitCount);
        Assert.Equal(0, UnitOfWork.RollbackCount);
    }

    // ---------------------------------------------------------
    // 2. Validation and security paths
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithNoEmail_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(email: null), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithEmptyEmail_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(email: "   "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsOkWithNeutralMessage()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("If an account exists", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_DoesNotQueueOtp()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueForgotPasswordOtp(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_DoesNotCreateOtpRecord()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var anyOtp = await Context.OtpVerifications.AnyAsync();
        Assert.False(anyOtp);
    }

    [Fact]
    public async Task Handle_WithUnverifiedUser_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("not verified", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithUnverifiedUser_DoesNotQueueOtp()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueForgotPasswordOtp(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 3. Regression: notification isolation
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenOtpQueueThrows_StillReturnsSuccess()
    {
        SetupUserExists();
        SetupAccountVerified();
        SetupOtpGeneration();

        _notifications
            .Setup(x => x.QueueForgotPasswordOtp(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Hangfire unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);

        var otp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserPublicId == UserPublicId);

        Assert.NotNull(otp);
    }

    // ---------------------------------------------------------
    // 4. Regression: rollback behaviour
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenSaveChangesThrows_RollsBackAndReturnsConflict()
    {
        SetupUserExists();
        SetupAccountVerified();
        SetupOtpGeneration();

        var failingUow = new Mock<Mova.Application.Interfaces.Persistence.IUnitOfWork>();

        failingUow
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        failingUow
            .Setup(x => x.AddAsync(It.IsAny<OtpVerification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        failingUow
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated DB failure"));

        failingUow
            .Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ForgotPasswordCommand.Handler(
            _identityService.Object,
            failingUow.Object,
            _otpService.Object,
            _notifications.Object,
            Mock.Of<ILogger<ForgotPasswordCommand.Handler>>());

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, result.StatusCode);
        failingUow.Verify(
            x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOtpGenerationThrows_RollsBackAndReturnsServerError()
    {
        SetupUserExists();
        SetupAccountVerified();

        _otpService
            .Setup(x => x.GenerateOtp())
            .Throws(new InvalidOperationException("Crypto RNG unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.RollbackCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }
}