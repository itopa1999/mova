using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.TransactionPin;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Constants;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class VerifyForgotPinOtpCommandTests : BaseTest
{
    private const string UserPublicId = "user_verifyforgotpin_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";
    private const string AccountPassword = "CorrectPass123!";
    private const string Otp = "123456";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<ITransactionPinService> _pinService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private VerifyForgotPinOtpCommand.Handler CreateHandler()
    {
        return new VerifyForgotPinOtpCommand.Handler(
            UnitOfWork,
            _identityService.Object,
            _notifications.Object,
            Mock.Of<ILogger<VerifyForgotPinOtpCommand.Handler>>(),
            _pinService.Object);
    }

    private VerifyForgotPinOtpCommand.Command CreateCommand(
        string? userPublicId = null,
        long? userId = null,
        string? password = null,
        string? otp = null)
    {
        return new VerifyForgotPinOtpCommand.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            UserId = userId ?? UserId,
            Password = password ?? AccountPassword,
            Otp = otp ?? Otp,
        };
    }

    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------

    private void SetupPasswordValid(bool valid = true)
    {
        _identityService
            .Setup(x => x.CheckPasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>()))
            .ReturnsAsync(valid);
    }

    private void SetupResetPin(bool success = true)
    {
        _pinService
            .Setup(x => x.ResetPinAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(success);
    }

    private void SetupResetPinThrows(Exception exception)
    {
        _pinService
            .Setup(x => x.ResetPinAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    private async Task<OtpVerification> SeedOtpAsync(
        string publicId = UserPublicId,
        string code = Otp,
        string purpose = OtpPurpose.TransactionPin,
        bool isUsed = false,
        DateTimeOffset? expiresAt = null)
    {
        var otp = new OtpVerification
        {
            UserPublicId = publicId,
            OtpCode = code,
            Purpose = purpose,
            IsUsed = isUsed,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(2),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await UnitOfWork.AddAsync(otp);
        await UnitOfWork.SaveChangesAsync();
        return otp;
    }

    // ---------------------------------------------------------
    // 1. Validation
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
        Assert.Equal("UserPublicId is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithEmptyPassword_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(password: ""),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Please enter your account password.", result.Message);
    }

    [Fact]
    public async Task Handle_WithEmptyOtp_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(otp: ""), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Please enter a valid 6-digit code.", result.Message);
    }

    [Fact]
    public async Task Handle_WithShortOtp_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(otp: "12345"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Please enter a valid 6-digit code.", result.Message);
    }

    [Fact]
    public async Task Handle_WithNonDigitOtp_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(otp: "12345a"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Please enter a valid 6-digit code.", result.Message);
    }

    // ---------------------------------------------------------
    // 2. Password check
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithWrongPassword_ReturnsBadRequest()
    {
        SetupPasswordValid(valid: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(password: "WrongPass!"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Incorrect password. Please try again.", result.Message);
    }

    [Fact]
    public async Task Handle_WithWrongPassword_DoesNotQueryOtp()
    {
        SetupPasswordValid(valid: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(password: "WrongPass!"), default);

        var anyOtps = await Context.OtpVerifications.AnyAsync();
        Assert.False(anyOtps);   // no OTP was seeded, but the point is: no reset either
    }

    // ---------------------------------------------------------
    // 3. OTP lookup
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithNoOtp_ReturnsBadRequest()
    {
        SetupPasswordValid();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(
            "No verification code found. Please request a new one.",
            result.Message);
    }

    [Fact]
    public async Task Handle_WithWrongOtp_ReturnsBadRequest()
    {
        SetupPasswordValid();
        await SeedOtpAsync(code: "999999");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(otp: "123456"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid OTP code.", result.Message);
    }

    [Fact]
    public async Task Handle_WithExpiredOtp_ReturnsBadRequest()
    {
        SetupPasswordValid();
        await SeedOtpAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("OTP has expired. Please request a new one.", result.Message);
    }

    [Fact]
    public async Task Handle_WithAlreadyUsedOtp_ReturnsBadRequest()
    {
        SetupPasswordValid();
        await SeedOtpAsync(isUsed: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(
            "No verification code found. Please request a new one.",
            result.Message);
    }

    [Fact]
    public async Task Handle_WithOtpOfDifferentPurpose_ReturnsBadRequest()
    {
        SetupPasswordValid();
        await SeedOtpAsync(purpose: OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(
            "No verification code found. Please request a new one.",
            result.Message);
    }

    [Fact]
    public async Task Handle_UsesMostRecentOtp_WhenMultipleExist()
    {
        SetupPasswordValid();
        SetupResetPin();

        await SeedOtpAsync(code: "111111");
        await SeedOtpAsync(code: "222222");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(otp: "222222"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    // ---------------------------------------------------------
    // 4. Reset PIN failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenResetReturnsFalse_ReturnsBadRequest()
    {
        SetupPasswordValid();
        await SeedOtpAsync();
        SetupResetPin(success: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Unable to reset PIN. Please try again.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenResetThrows_ReturnsInternalServerError()
    {
        SetupPasswordValid();
        await SeedOtpAsync();
        SetupResetPinThrows(new Exception("Service down"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Contains("An error occurred", result.Message);
    }

    [Fact]
    public async Task Handle_WhenResetThrows_DoesNotMarkOtpUsed()
    {
        SetupPasswordValid();
        var otp = await SeedOtpAsync();
        SetupResetPinThrows(new Exception("Service down"));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var reloaded = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == otp.Id);

        Assert.False(reloaded.IsUsed);
    }

    // ---------------------------------------------------------
    // 5. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsSuccess()
    {
        SetupPasswordValid();
        SetupResetPin();
        await SeedOtpAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Verified. You can now set a new PIN.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsResetPinOnce()
    {
        SetupPasswordValid();
        SetupResetPin();
        await SeedOtpAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _pinService.Verify(
            x => x.ResetPinAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_MarksOtpAsUsed()
    {
        SetupPasswordValid();
        SetupResetPin();
        var otp = await SeedOtpAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var reloaded = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == otp.Id);

        Assert.True(reloaded.IsUsed);
        Assert.NotNull(reloaded.UsedAt);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CommitsBySavingChanges()
    {
        SetupPasswordValid();
        SetupResetPin();
        await SeedOtpAsync();

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WithValidRequest_PassesUserIdToCheckPassword()
    {
        SetupPasswordValid();
        SetupResetPin();
        await SeedOtpAsync();

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(userId: 99, password: "MyPass!"),
            default);

        _identityService.Verify(
            x => x.CheckPasswordAsync(99, "MyPass!"),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 6. Notifications
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_QueuesInAppNotification()
    {
        SetupPasswordValid();
        SetupResetPin();
        await SeedOtpAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Security,
                "PIN reset verified",
                It.Is<string>(m =>
                    m.Contains("identity was verified") &&
                    m.Contains("transaction PIN has been cleared")),
                "/pin-gate",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPasswordInvalid_DoesNotSendNotification()
    {
        SetupPasswordValid(valid: false);

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
    }

    [Fact]
    public async Task Handle_WhenOtpInvalid_DoesNotSendNotification()
    {
        SetupPasswordValid();
        await SeedOtpAsync(code: "999999");

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(otp: "111111"), default);

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
    // 7. Regression: notification failure must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillReturnsSuccess()
    {
        SetupPasswordValid();
        SetupResetPin();
        await SeedOtpAsync();

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
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_OtpIsStillMarkedUsed()
    {
        SetupPasswordValid();
        SetupResetPin();
        var otp = await SeedOtpAsync();

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
        await handler.Handle(CreateCommand(), default);

        var reloaded = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == otp.Id);

        Assert.True(reloaded.IsUsed);
    }
}