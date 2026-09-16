using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;

namespace Mova.Tests.Handlers;

public sealed class ResendVerificationOtpCommandTests : BaseTest
{
    private const string UserPublicId = "user_resend_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserPhone = "08050000000";
    private const string GeneratedOtp = "654321";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IOtpService> _otpService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private ResendVerificationOtpCommand.Handler CreateHandler()
    {
        return new ResendVerificationOtpCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            _otpService.Object,
            _notifications.Object,
            Mock.Of<ILogger<ResendVerificationOtpCommand.Handler>>());
    }

    private ResendVerificationOtpCommand.Command CreateCommand(
        string email = UserEmail,
        string purpose = "account-verification")
    {
        return new ResendVerificationOtpCommand.Command
        {
            Email = email,
            Purpose = purpose,
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

    private void SetupAccountVerified(bool verified = false)
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

    private async Task<OtpVerification> SeedOtpAsync(
        string purpose,
        bool isUsed = false,
        string publicId = UserPublicId)
    {
        var otp = new OtpVerification
        {
            UserPublicId = publicId,
            OtpCode = "111111",
            Purpose = purpose,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            IsUsed = isUsed,
        };

        await UnitOfWork.AddAsync(otp);
        await UnitOfWork.SaveChangesAsync();

        return otp;
    }

    // ---------------------------------------------------------
    // 1. Happy path — account verification
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithAccountVerification_SavesNewOtp()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);

        var newOtp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.UserPublicId == UserPublicId
                && x.Purpose == OtpPurpose.AccountVerification
                && !x.IsUsed);

        Assert.NotNull(newOtp);
        Assert.Equal(GeneratedOtp, newOtp!.OtpCode);
        Assert.True(newOtp.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithAccountVerification_InvalidatesExistingOtps()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        var oldOtp1 = await SeedOtpAsync(OtpPurpose.AccountVerification);
        var oldOtp2 = await SeedOtpAsync(OtpPurpose.AccountVerification);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after1 = await Context.OtpVerifications.AsNoTracking().FirstAsync(x => x.Id == oldOtp1.Id);
        var after2 = await Context.OtpVerifications.AsNoTracking().FirstAsync(x => x.Id == oldOtp2.Id);

        Assert.True(after1.IsUsed);
        Assert.True(after2.IsUsed);
    }

    [Fact]
    public async Task Handle_WithAccountVerification_DoesNotInvalidatePasswordResetOtps()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        var accountOtp = await SeedOtpAsync(OtpPurpose.AccountVerification);
        var passwordResetOtp = await SeedOtpAsync(OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var afterAccount = await Context.OtpVerifications.AsNoTracking().FirstAsync(x => x.Id == accountOtp.Id);
        var afterPasswordReset = await Context.OtpVerifications.AsNoTracking().FirstAsync(x => x.Id == passwordResetOtp.Id);

        Assert.True(afterAccount.IsUsed);
        Assert.False(afterPasswordReset.IsUsed);
    }

    [Fact]
    public async Task Handle_WithAccountVerification_QueuesOtpDelivery()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueOtpDelivery(
                "Lucky",
                UserEmail,
                UserPhone,
                GeneratedOtp,
                It.Is<string>(s => s.Contains("ACCOUNT_VERIFICATION"))),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithAccountVerification_ReturnsSuccessWithUserPublicId()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
    }

    [Fact]
    public async Task Handle_WithAccountVerification_CommitsTransactionOnce()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.CommitCount);
        Assert.Equal(0, UnitOfWork.RollbackCount);
    }

    // ---------------------------------------------------------
    // 2. Happy path — password reset
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithPasswordReset_SavesPasswordResetOtp()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(purpose: "password-reset"), default);

        Assert.True(result.IsSuccess);

        var newOtp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.UserPublicId == UserPublicId
                && x.Purpose == OtpPurpose.PasswordReset
                && !x.IsUsed);

        Assert.NotNull(newOtp);
        Assert.Equal(GeneratedOtp, newOtp!.OtpCode);
    }

    [Fact]
    public async Task Handle_WithPasswordReset_DoesNotCheckAccountVerification()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(purpose: "password-reset"), default);

        _identityService.Verify(
            x => x.IsAccountVerifiedAsync(It.IsAny<long>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithPasswordReset_DoesNotInvalidateAccountVerificationOtps()
    {
        SetupUserExists();
        SetupOtpGeneration();

        var accountOtp = await SeedOtpAsync(OtpPurpose.AccountVerification);
        var passwordResetOtp = await SeedOtpAsync(OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(purpose: "password-reset"), default);

        var afterAccount = await Context.OtpVerifications.AsNoTracking().FirstAsync(x => x.Id == accountOtp.Id);
        var afterPasswordReset = await Context.OtpVerifications.AsNoTracking().FirstAsync(x => x.Id == passwordResetOtp.Id);

        Assert.False(afterAccount.IsUsed);
        Assert.True(afterPasswordReset.IsUsed);
    }

    // ---------------------------------------------------------
    // 3. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyEmail_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(email: "   "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
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
    public async Task Handle_WithAlreadyVerifiedAccount_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupAccountVerified(verified: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("already verified", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithAlreadyVerifiedAccount_DoesNotQueueOtp()
    {
        SetupUserExists();
        SetupAccountVerified(verified: true);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueOtpDelivery(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithUnknownPurpose_DefaultsToAccountVerification()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(purpose: "something-weird"), default);

        var newOtp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.UserPublicId == UserPublicId
                && x.Purpose == OtpPurpose.AccountVerification
                && !x.IsUsed);

        Assert.NotNull(newOtp);
    }

    // ---------------------------------------------------------
    // 4. Regression: notification failures must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenOtpQueueThrows_StillReturnsSuccess()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        _notifications
            .Setup(x => x.QueueOtpDelivery(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Hangfire unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenOtpQueueThrows_NewOtpStillPersisted()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        _notifications
            .Setup(x => x.QueueOtpDelivery(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new Exception("boom"));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var newOtp = await Context.OtpVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.UserPublicId == UserPublicId
                && x.Purpose == OtpPurpose.AccountVerification
                && !x.IsUsed);

        Assert.NotNull(newOtp);
    }

    // ---------------------------------------------------------
    // 5. Regression: rollback behaviour
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenSaveChangesThrows_RollsBackAndReturnsConflict()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);
        SetupOtpGeneration();

        var failingUow = new Mock<Mova.Application.Interfaces.Persistence.IUnitOfWork>();

        failingUow
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        failingUow
            .Setup(x => x.Query<OtpVerification>())
            .Returns(UnitOfWork.Query<OtpVerification>());

        failingUow
            .Setup(x => x.Update(It.IsAny<OtpVerification>()))
            .Callback<OtpVerification>(o => UnitOfWork.Update(o));

        failingUow
            .Setup(x => x.AddAsync(It.IsAny<OtpVerification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        failingUow
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated DB failure"));

        failingUow
            .Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new ResendVerificationOtpCommand.Handler(
            _identityService.Object,
            failingUow.Object,
            _otpService.Object,
            _notifications.Object,
            Mock.Of<ILogger<ResendVerificationOtpCommand.Handler>>());

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
        SetupAccountVerified(verified: false);

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