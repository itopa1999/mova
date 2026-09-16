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

public sealed class VerifyAccountCommandTests : BaseTest
{
    private const string UserPublicId = "user_verify_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserPhone = "08050000000";
    private const string ValidOtp = "123456";
    private const string AccessToken = "access.jwt.token";
    private const string RefreshTokenValue = "refresh-token-value";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<INotificationQueue> _notifications = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGenerator = new();
    private readonly Mock<IRefreshTokenService> _refreshTokenService = new();

    private VerifyAccountCommand.Handler CreateHandler()
    {
        return new VerifyAccountCommand.Handler(
            UnitOfWork,
            _identityService.Object,
            Mock.Of<ILogger<VerifyAccountCommand.Handler>>(),
            _notifications.Object,
            _jwtGenerator.Object,
            _refreshTokenService.Object);
    }

    private VerifyAccountCommand.Command CreateCommand(
        string email = UserEmail,
        string otp = ValidOtp,
        string platform = Platforms.Web)
    {
        return new VerifyAccountCommand.Command
        {
            Email = email,
            OtpCode = otp,
            Platform = platform,
        };
    }

    private void SetupUserExists(
        long userId = UserId,
        string publicId = UserPublicId,
        string? email = UserEmail,
        string? phone = UserPhone,
        string? profilePicture = null)
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
                profilePicture,
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

    private void SetupMarkVerifiedSucceeds()
    {
        _identityService
            .Setup(x => x.MarkEmailAndPhoneAsVerifiedAsync(It.IsAny<long>()))
            .ReturnsAsync((true, string.Empty));
    }

    private void SetupMarkVerifiedFails(string error = "Mark failed")
    {
        _identityService
            .Setup(x => x.MarkEmailAndPhoneAsVerifiedAsync(It.IsAny<long>()))
            .ReturnsAsync((false, error));
    }

    private void SetupRoles(params string[] roles)
    {
        _identityService
            .Setup(x => x.GetRolesAsync(It.IsAny<long>()))
            .ReturnsAsync(roles.ToList());
    }

    private void SetupJwtGeneration()
    {
        _jwtGenerator
            .Setup(x => x.GenerateToken(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<decimal>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>>()))
            .Returns(AccessToken);
    }

    private void SetupRefreshTokenCreation()
    {
        _refreshTokenService
            .Setup(x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenValue, new RefreshToken
            {
                UserPublicId = UserPublicId,
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                CreatedAt = DateTimeOffset.UtcNow,
                RevokedAt = null,
            }));
    }

    private async Task<OtpVerification> SeedOtpAsync(
        string otpCode = ValidOtp,
        bool isUsed = false,
        bool expired = false,
        string purpose = OtpPurpose.AccountVerification,
        string publicId = UserPublicId)
    {
        var otp = new OtpVerification
        {
            UserPublicId = publicId,
            OtpCode = otpCode,
            Purpose = purpose,
            ExpiresAt = expired
                ? DateTimeOffset.UtcNow.AddMinutes(-1)
                : DateTimeOffset.UtcNow.AddMinutes(2),
            CreatedAt = DateTimeOffset.UtcNow,
            IsUsed = isUsed,
        };

        await UnitOfWork.AddAsync(otp);
        await UnitOfWork.SaveChangesAsync();

        return otp;
    }

    private void SetupHappyPath()
    {
        SetupUserExists();
        SetupMarkVerifiedSucceeds();
        SetupRoles();
        SetupJwtGeneration();
        SetupRefreshTokenCreation();
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidOtp_ReturnsSuccess()
    {
        SetupHappyPath();
        await SeedOtpAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess, 
        $"Status={result.StatusCode}, Message={result.Message}, Errors={string.Join(",", result.Message)}");
        Assert.Equal(System.Net.HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Account verified successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidOtp_ReturnsUserAndTokens()
    {
        SetupHappyPath();
        await SeedOtpAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
        Assert.True(result.Data.IsAccountVerified);
        Assert.Equal(UserEmail, result.Data.Email);
        Assert.Equal(AccessToken, result.Data.AccessToken);
        Assert.Equal(RefreshTokenValue, result.Data.RefreshToken);
        Assert.Equal(Platforms.Web, result.Data.Platform);
        Assert.True(result.Data.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithValidOtp_MarksOtpAsUsed()
    {
        SetupHappyPath();
        var otp = await SeedOtpAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == otp.Id);

        Assert.True(after.IsUsed);
        Assert.NotNull(after.UsedAt);
    }

    [Fact]
    public async Task Handle_WithValidOtp_PersistsRefreshToken()
    {
        SetupHappyPath();
        await SeedOtpAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var savedToken = await Context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserPublicId == UserPublicId);

        Assert.NotNull(savedToken);
        Assert.Null(savedToken!.RevokedAt);
    }

    [Fact]
    public async Task Handle_WithValidOtp_QueuesWelcomeEmail()
    {
        SetupHappyPath();
        await SeedOtpAsync();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueWelcomeEmail("Lucky", UserEmail),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidOtp_CommitsTransactionOnce()
    {
        SetupHappyPath();
        await SeedOtpAsync();

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
    public async Task Handle_WithInvalidPlatform_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(platform: "Unknown"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid platform", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

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
    public async Task Handle_WithNoOtp_ReturnsBadRequest()
    {
        SetupUserExists();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid OTP", result.Message);
        Assert.Equal(1, UnitOfWork.RollbackCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Handle_WithWrongOtp_ReturnsBadRequest()
    {
        SetupUserExists();
        await SeedOtpAsync(otpCode: "999999");

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(otp: ValidOtp), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid OTP", result.Message);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WithExpiredOtp_ReturnsBadRequest()
    {
        SetupUserExists();
        await SeedOtpAsync(expired: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("expired", result.Message);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WithAlreadyUsedOtp_ReturnsBadRequest()
    {
        SetupUserExists();
        await SeedOtpAsync(isUsed: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WhenMarkVerifiedFails_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupMarkVerifiedFails("Identity update failed");
        await SeedOtpAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Identity update failed", result.Message);
        Assert.Equal(1, UnitOfWork.RollbackCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    // ---------------------------------------------------------
    // 3. Regression: OTP filtering
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_DoesNotAcceptOtpFromDifferentPurpose()
    {
        SetupUserExists();
        await SeedOtpAsync(purpose: OtpPurpose.PasswordReset);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid OTP", result.Message);
    }

    [Fact]
    public async Task Handle_DoesNotAcceptOtpFromDifferentUser()
    {
        SetupUserExists();
        await SeedOtpAsync(publicId: "someone_else");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid OTP", result.Message);
    }

    [Fact]
    public async Task Handle_UsesMostRecentOtp_WhenMultipleExist()
    {
        SetupUserExists();
        SetupMarkVerifiedSucceeds();
        SetupRoles();
        SetupJwtGeneration();
        SetupRefreshTokenCreation();

        var older = await SeedOtpAsync(otpCode: "111111");
        var newer = await SeedOtpAsync(otpCode: "222222");

        older.CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        newer.CreatedAt = DateTimeOffset.UtcNow;
        UnitOfWork.Update(older);
        UnitOfWork.Update(newer);
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(otp: "222222"), default);

        Assert.True(result.IsSuccess);
    }

    // ---------------------------------------------------------
    // 4. Regression: notification failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenWelcomeEmailQueueThrows_StillReturnsSuccess()
    {
        SetupUserExists();
        SetupMarkVerifiedSucceeds();
        SetupRoles();
        SetupJwtGeneration();
        SetupRefreshTokenCreation();
        await SeedOtpAsync();

        _notifications
            .Setup(x => x.QueueWelcomeEmail(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Hangfire unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WhenWelcomeEmailQueueThrows_StillMarksAccountVerified()
    {
        SetupUserExists();
        SetupMarkVerifiedSucceeds();
        SetupRoles();
        SetupJwtGeneration();
        SetupRefreshTokenCreation();
        var otp = await SeedOtpAsync();

        _notifications
            .Setup(x => x.QueueWelcomeEmail(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new Exception("boom"));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after = await Context.OtpVerifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == otp.Id);

        Assert.True(after.IsUsed);
    }

    // ---------------------------------------------------------
    // 5. Regression: rollback behaviour
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenSaveChangesThrows_RollsBackAndReturnsConflict()
    {
        SetupUserExists();
        SetupMarkVerifiedSucceeds();
        await SeedOtpAsync();

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
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated failure"));

        failingUow
            .Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new VerifyAccountCommand.Handler(
            failingUow.Object,
            _identityService.Object,
            Mock.Of<ILogger<VerifyAccountCommand.Handler>>(),
            _notifications.Object,
            _jwtGenerator.Object,
            _refreshTokenService.Object);

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, result.StatusCode);
        failingUow.Verify(
            x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}