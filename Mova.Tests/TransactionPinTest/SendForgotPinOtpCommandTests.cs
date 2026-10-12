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
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class SendForgotPinOtpCommandTests : BaseTest
{
    private const string UserPublicId = "user_forgotpin_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";
    private const string UserPhone = "08050000000";
    private const string GeneratedOtp = "123456";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IOtpService> _otpService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    public SendForgotPinOtpCommandTests()
    {
        SetupPermissions();
    }

    private SendForgotPinOtpCommand.Handler CreateHandler()
    {
        return new SendForgotPinOtpCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            _otpService.Object,
            _notifications.Object,
            Mock.Of<ILogger<SendForgotPinOtpCommand.Handler>>());
    }

    private SendForgotPinOtpCommand.Command CreateCommand(
        string? userPublicId = null,
        string platform = "web")
    {
        return new SendForgotPinOtpCommand.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            Platform = platform,
        };
    }

    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------

    private void SetupUserExists(
        long userId = UserId,
        string publicId = UserPublicId,
        string email = UserEmail,
        string firstName = UserFirstName,
        string phone = UserPhone)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                userId,
                publicId,
                firstName,
                null,
                "Starboy",
                email,
                phone,
                null,
                Money.FromNaira(0),
                string.Empty,
                false,
                false,
                false,
                false,
                string.Empty,
                DateTimeOffset.UtcNow));
    }

    private void SetupUserNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);
    }

    private void SetupOtpGeneration(string otp = GeneratedOtp)
    {
        _otpService
            .Setup(x => x.GenerateOtp())
            .Returns(otp);
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

    private async Task<OtpVerification> SeedExistingOtpAsync(
        string publicId = UserPublicId,
        string purpose = OtpPurpose.TransactionPin,
        bool isUsed = false)
    {
        var otp = new OtpVerification
        {
            UserPublicId = publicId,
            OtpCode = "999999",
            Purpose = purpose,
            IsUsed = isUsed,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
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

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_DoesNotCheckPermissions()
    {
        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(userPublicId: ""),
            default);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 2. User lookup
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsNotFound()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_DoesNotGenerateOtp()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _otpService.Verify(
            x => x.GenerateOtp(),
            Times.Never);

        _notifications.Verify(
            x => x.QueueOtpDelivery(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 3. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsSuccess()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(
            "Verification code sent to your email and phone.",
            result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_GeneratesOtpOnce()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _otpService.Verify(
            x => x.GenerateOtp(),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_PersistsOtp()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var saved = await Context.OtpVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.UserPublicId == UserPublicId &&
                x.OtpCode == GeneratedOtp &&
                x.Purpose == OtpPurpose.TransactionPin);

        Assert.NotNull(saved);
        Assert.False(saved!.IsUsed);
        Assert.True(saved.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SetsTwoMinuteExpiry()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var before = DateTimeOffset.UtcNow;
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);
        var after = DateTimeOffset.UtcNow;

        var saved = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.UserPublicId == UserPublicId);

        Assert.True(saved.ExpiresAt >= before.AddMinutes(2));
        Assert.True(saved.ExpiresAt <= after.AddMinutes(2));
    }

    [Fact]
    public async Task Handle_WithValidRequest_QueuesOtpDelivery()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueOtpDelivery(
                UserFirstName,
                UserEmail,
                UserPhone,
                GeneratedOtp,
                OtpPurpose.TransactionPin),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SavesChangesOnce()
    {
        SetupUserExists();
        SetupOtpGeneration();

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }

    // ---------------------------------------------------------
    // 4. Invalidation of existing OTPs
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithExistingOtp_MarksOldOtpsAsUsed()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var oldOtp = await SeedExistingOtpAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var reloaded = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == oldOtp.Id);

        Assert.True(reloaded.IsUsed);
    }

    [Fact]
    public async Task Handle_WithMultipleExistingOtps_MarksAllAsUsed()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var old1 = await SeedExistingOtpAsync();
        var old2 = await SeedExistingOtpAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var reloaded1 = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == old1.Id);
        var reloaded2 = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == old2.Id);

        Assert.True(reloaded1.IsUsed);
        Assert.True(reloaded2.IsUsed);
    }

    [Fact]
    public async Task Handle_WithExistingOtpOfDifferentPurpose_DoesNotMarkItUsed()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var otherPurposeOtp = await SeedExistingOtpAsync(
            purpose: OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var reloaded = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == otherPurposeOtp.Id);

        Assert.False(reloaded.IsUsed);
    }

    [Fact]
    public async Task Handle_WithExistingOtpOfDifferentUser_DoesNotMarkItUsed()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var otherUserOtp = await SeedExistingOtpAsync(
            publicId: "someone_else");

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var reloaded = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == otherUserOtp.Id);

        Assert.False(reloaded.IsUsed);
    }

    [Fact]
    public async Task Handle_WithExistingUsedOtp_LeavesItUntouched()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var alreadyUsed = await SeedExistingOtpAsync(isUsed: true);
        var originalUsedAt = alreadyUsed.UsedAt;

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var reloaded = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == alreadyUsed.Id);

        Assert.True(reloaded.IsUsed);
        Assert.Equal(originalUsedAt, reloaded.UsedAt);
    }

    // ---------------------------------------------------------
    // 5. Regression: multiple invocations
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCalledTwice_CreatesTwoOtps()
    {
        SetupUserExists();
        SetupOtpGeneration("111111");

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        SetupOtpGeneration("222222");
        await handler.Handle(CreateCommand(), default);

        var all = await Context.OtpVerifications
            .AsNoTracking()
            .Where(x => x.UserPublicId == UserPublicId)
            .ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, x => x.OtpCode == "111111");
        Assert.Contains(all, x => x.OtpCode == "222222");
    }

    [Fact]
    public async Task Handle_WhenCalledTwice_SecondInvocationInvalidatesFirstOtp()
    {
        SetupUserExists();

        SetupOtpGeneration("111111");
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        SetupOtpGeneration("222222");
        await handler.Handle(CreateCommand(), default);

        var firstOtp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.OtpCode == "111111");

        var secondOtp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.OtpCode == "222222");

        Assert.True(firstOtp.IsUsed);
        Assert.False(secondOtp.IsUsed);
    }

    // ---------------------------------------------------------
    // 6. Permissions gating
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
    public async Task Handle_WhenPermissionsUnavailable_DoesNotLookupUser()
    {
        SetupPermissionsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotGenerateOtp()
    {
        SetupPermissionsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _otpService.Verify(
            x => x.GenerateOtp(),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotSendNotification()
    {
        SetupPermissionsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueOtpDelivery(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotPersistOtp()
    {
        SetupPermissionsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var count = await Context.OtpVerifications.CountAsync();
        Assert.Equal(0, count);
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
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotLookupUser()
    {
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotGenerateOtp()
    {
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _otpService.Verify(
            x => x.GenerateOtp(),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotPersistOtp()
    {
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var count = await Context.OtpVerifications.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotSendNotification()
    {
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueOtpDelivery(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSensitiveOperationsBlocked_DoesNotInvalidateExistingOtps()
    {
        var existing = await SeedExistingOtpAsync();
        SetupPermissions(canPerformSensitiveOperations: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var reloaded = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == existing.Id);

        Assert.False(reloaded.IsUsed);
    }

    [Fact]
    public async Task Handle_WhenPermissionsGranted_ProceedsToUserLookup()
    {
        SetupPermissions(canPerformSensitiveOperations: true);
        SetupUserExists();
        SetupOtpGeneration();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}