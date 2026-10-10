using System.Net;
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

namespace Mova.Tests.Handlers;

public sealed class LoginUserCommandTests : BaseTest
{
    private const string UserPublicId = "user_login_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserPhone = "08050000000";
    private const string ValidPassword = "ValidPass123!";
    private const string AccessToken = "access.jwt.token";
    private const string RefreshTokenValue = "refresh-token-value";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGenerator = new();
    private readonly Mock<IRefreshTokenService> _refreshTokenService = new();

    private LoginUserCommand.Handler CreateHandler()
    {
        return new LoginUserCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            _jwtGenerator.Object,
            _refreshTokenService.Object,
            Mock.Of<INotificationQueue>(),
            Mock.Of<ILogger<LoginUserCommand.Handler>>());
    }

    private LoginUserCommand.Command CreateCommand(
        string identifier = UserEmail,
        string password = ValidPassword,
        string platform = Platforms.Web)
    {
        return new LoginUserCommand.Command
        {
            Identifier = identifier,
            Password = password,
            Platform = platform,
        };
    }

    // ---------------------------------------------------------
    // Permissions mock helpers
    // ---------------------------------------------------------

    private static UserPermissionsDto BuildPermissions(
        UserAccountStatus status = UserAccountStatus.Active,
        string statusLabel = "Active",
        string statusDescription = "Your account is in good standing and fully operational.",
        bool canLogin = true,
        bool canCreateWallets = true,
        bool canPerformSensitiveOperations = true,
        bool canReceivePayouts = true,
        bool canTopUp = true,
        string? restrictionReason = null,
        string? restrictionReasonDetails = null,
        DateTimeOffset? restrictionExpiresAt = null)
        => new(
            UserPublicId,
            status,
            statusLabel,
            statusDescription,
            canLogin,
            canCreateWallets,
            canPerformSensitiveOperations,
            canReceivePayouts,
            canTopUp,
            restrictionReason,
            restrictionReasonDetails,
            null,
            restrictionExpiresAt);

    private void SetupPermissions(UserPermissionsDto? permissions = null)
    {
        _identityService
            .Setup(x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions ?? BuildPermissions());
    }

    private void SetupPermissionsReturnsNull()
    {
        _identityService
            .Setup(x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPermissionsDto?)null);
    }

    private void SetupBlockedByStatus(
        UserAccountStatus status,
        string statusLabel,
        string statusDescription,
        string? restrictionReason = null,
        string? restrictionReasonDetails = null,
        DateTimeOffset? restrictionExpiresAt = null)
    {
        SetupPermissions(BuildPermissions(
            status: status,
            statusLabel: statusLabel,
            statusDescription: statusDescription,
            canLogin: false,
            canCreateWallets: false,
            canPerformSensitiveOperations: false,
            canReceivePayouts: false,
            restrictionReason: restrictionReason,
            restrictionReasonDetails: restrictionReasonDetails,
            restrictionExpiresAt: restrictionExpiresAt));
    }

    // ---------------------------------------------------------
    // User & credentials helpers
    // ---------------------------------------------------------

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

    private void SetupAccountVerified(bool verified = true)
    {
        _identityService
            .Setup(x => x.IsAccountVerifiedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(verified);
    }

    private void SetupPasswordCheck(bool valid = true)
    {
        _identityService
            .Setup(x => x.CheckPasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(valid);
    }

    private void SetupRoles(params string[] roles)
    {
        _identityService
            .Setup(x => x.GetRolesAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
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

    // ---------------------------------------------------------
    // Lockout helpers
    // ---------------------------------------------------------

    private void SetupNotLockedOut()
    {
        _identityService
            .Setup(x => x.IsLockedOutAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _identityService
            .Setup(x => x.GetLockoutEndAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);
    }

    private void SetupLockedOut(DateTimeOffset lockoutEnd)
    {
        _identityService
            .Setup(x => x.IsLockedOutAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _identityService
            .Setup(x => x.GetLockoutEndAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockoutEnd);
    }

    private void SetupRecordFailedAccess()
    {
        _identityService
            .Setup(x => x.RecordFailedAccessAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupResetFailedAccess()
    {
        _identityService
            .Setup(x => x.ResetFailedAccessAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ---------------------------------------------------------
    // Composite setups
    // ---------------------------------------------------------

    private void SetupHappyPath()
    {
        SetupUserExists();
        SetupPermissions();
        SetupNotLockedOut();
        SetupAccountVerified();
        SetupPasswordCheck();
        SetupRoles();
        SetupResetFailedAccess();
        SetupJwtGeneration();
        SetupRefreshTokenCreation();
    }

    private void SetupWrongPasswordNotLocked()
    {
        SetupUserExists();
        SetupPermissions();
        SetupNotLockedOut();
        SetupAccountVerified();
        SetupPasswordCheck(valid: false);
        SetupRecordFailedAccess();
    }

    private void SetupWrongPasswordThatTriggersLockout(DateTimeOffset lockoutEnd)
    {
        SetupUserExists();
        SetupPermissions();

        _identityService
            .SetupSequence(x => x.IsLockedOutAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        _identityService
            .Setup(x => x.GetLockoutEndAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockoutEnd);

        SetupAccountVerified();
        SetupPasswordCheck(valid: false);
        SetupRecordFailedAccess();
    }

    // =========================================================
    // 1. Happy path
    // =========================================================

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsSuccess()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Login successful.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_DataIsPopulated()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.Data);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_BlockedIsNull()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.Null(result.Data!.Blocked);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsAllLoginFields()
    {
        SetupUserExists(profilePicture: "https://example.com/avatar.png");
        SetupPermissions();
        SetupNotLockedOut();
        SetupAccountVerified();
        SetupPasswordCheck();
        SetupRoles("User");
        SetupResetFailedAccess();
        SetupJwtGeneration();
        SetupRefreshTokenCreation();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data?.Data);

        var login = result.Data!.Data!;

        Assert.Equal(UserPublicId, login.UserPublicId);
        Assert.Equal(UserEmail, login.Email);
        Assert.Equal(UserPhone, login.Phone);
        Assert.Equal("Lucky Starboy", login.FullName);
        Assert.Equal("https://example.com/avatar.png", login.ProfilePicture);
        Assert.Equal(Platforms.Web, login.Platform);
        Assert.Equal(AccessToken, login.AccessToken);
        Assert.Equal(RefreshTokenValue, login.RefreshToken);
        Assert.True(login.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_SavesRefreshTokenToDatabase()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);

        var savedToken = await Context.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserPublicId == UserPublicId);

        Assert.NotNull(savedToken);
        Assert.Null(savedToken!.RevokedAt);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_CallsJwtGeneratorOnce()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _jwtGenerator.Verify(
            x => x.GenerateToken(
                UserId,
                UserPublicId,
                "Lucky",
                null,
                "Starboy",
                UserEmail,
                UserPhone,
                It.IsAny<decimal>(),
                "Lucky Starboy",
                Platforms.Web,
                It.IsAny<List<string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithMobilePlatform_Succeeds()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(platform: Platforms.Mobile), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Platforms.Mobile, result.Data!.Data!.Platform);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_PersistsRefreshTokenOnce()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }

    // =========================================================
    // 2. Validation failures
    // =========================================================

    [Fact]
    public async Task Handle_WithInvalidPlatform_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(platform: "UnknownPlatform"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid platform", result.Message);
    }

    [Fact]
    public async Task Handle_WithInvalidPlatform_DoesNotCheckPermissions()
    {
        var handler = CreateHandler();

        await handler.Handle(
            CreateCommand(platform: "UnknownPlatform"), default);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsBadRequestWithGenericMessage()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid email or password.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_DoesNotCheckPermissions()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithUnverifiedAccount_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupPermissions();
        SetupNotLockedOut();
        SetupAccountVerified(verified: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("verify your account", result.Message);
    }

    [Fact]
    public async Task Handle_WithWrongPassword_ReturnsBadRequestWithGenericMessage()
    {
        SetupWrongPasswordNotLocked();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid email or password.", result.Message);
    }

    [Fact]
    public async Task Handle_WithWrongPassword_DataIsNull()
    {
        SetupWrongPasswordNotLocked();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_DoesNotGenerateToken()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _jwtGenerator.Verify(
            x => x.GenerateToken(
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
                It.IsAny<List<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithWrongPassword_DoesNotCreateRefreshToken()
    {
        SetupWrongPasswordNotLocked();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _refreshTokenService.Verify(
            x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithUnverifiedAccount_DoesNotCreateRefreshToken()
    {
        SetupUserExists();
        SetupPermissions();
        SetupNotLockedOut();
        SetupAccountVerified(verified: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _refreshTokenService.Verify(
            x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_DoesNotSaveAnyRefreshToken()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var anyToken = await Context.RefreshTokens.AnyAsync();
        Assert.False(anyToken);
    }

    // =========================================================
    // 3. Security: generic messages don't leak account existence
    // =========================================================

    [Fact]
    public async Task Handle_UnknownUserAndWrongPassword_ReturnIdenticalMessages()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var unknownUserResult = await handler.Handle(CreateCommand(), default);

        ResetDatabase();

        _identityService.Reset();
        SetupWrongPasswordNotLocked();

        var wrongPasswordResult = await handler.Handle(CreateCommand(), default);

        Assert.Equal(unknownUserResult.Message, wrongPasswordResult.Message);
        Assert.Equal(unknownUserResult.StatusCode, wrongPasswordResult.StatusCode);
    }

    // =========================================================
    // 4. Permissions lookup
    // =========================================================

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupPermissionsReturnsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid email or password.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotCheckLockout()
    {
        SetupUserExists();
        SetupPermissionsReturnsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.IsLockedOutAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_DoesNotCheckPassword()
    {
        SetupUserExists();
        SetupPermissionsReturnsNull();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.CheckPasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================
    // 5. Blocked by status (Suspended / Closed / Deactivated)
    // =========================================================

    [Fact]
    public async Task Handle_WhenAccountSuspended_ReturnsForbidden()
    {
        SetupUserExists();
        SetupBlockedByStatus(
            UserAccountStatus.Suspended,
            "Suspended",
            "Your account is suspended. Login is disabled while we review the matter.",
            restrictionReason: "SuspectedFraud",
            restrictionReasonDetails: "Chargebacks from 4 cards.");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenAccountSuspended_ReturnsBlockedDto()
    {
        SetupUserExists();
        SetupBlockedByStatus(
            UserAccountStatus.Suspended,
            "Suspended",
            "Your account is suspended. Login is disabled while we review the matter.",
            restrictionReason: "SuspectedFraud",
            restrictionReasonDetails: "Chargebacks from 4 cards.");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.Blocked);
        Assert.Null(result.Data.Data);

        var blocked = result.Data.Blocked!;

        Assert.Equal(UserPublicId, blocked.UserPublicId);
        Assert.Equal("Suspended", blocked.AccountStatus);
        Assert.Equal("Suspended", blocked.StatusLabel);
        Assert.Equal(
            "Your account is suspended. Login is disabled while we review the matter.",
            blocked.StatusDescription);
        Assert.Equal("SuspectedFraud", blocked.RestrictionReason);
        Assert.Equal("Chargebacks from 4 cards.", blocked.RestrictionReasonDetails);
        Assert.Null(blocked.RestrictionExpiresAt);
        Assert.Null(blocked.LockoutEndsAt);
    }

    [Fact]
    public async Task Handle_WhenAccountClosed_ReturnsForbiddenWithBlockedDto()
    {
        SetupUserExists();
        SetupBlockedByStatus(
            UserAccountStatus.Closed,
            "Closed",
            "Your account is permanently closed.",
            restrictionReason: "UserRequestedDeactivation",
            restrictionReasonDetails: "Requested via support.");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.NotNull(result.Data?.Blocked);
        Assert.Equal("Closed", result.Data!.Blocked!.StatusLabel);
    }

    [Fact]
    public async Task Handle_WhenAccountDeactivated_ReturnsForbiddenWithBlockedDto()
    {
        SetupUserExists();
        SetupBlockedByStatus(
            UserAccountStatus.Deactivated,
            "Deactivated",
            "Your account is deactivated. Contact support to reactivate it.");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.NotNull(result.Data?.Blocked);
        Assert.Equal("Deactivated", result.Data!.Blocked!.StatusLabel);
    }

    [Fact]
    public async Task Handle_WhenAccountBlocked_DoesNotCheckLockout()
    {
        SetupUserExists();
        SetupBlockedByStatus(
            UserAccountStatus.Suspended,
            "Suspended",
            "Account suspended.");

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.IsLockedOutAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAccountBlocked_DoesNotCheckPassword()
    {
        SetupUserExists();
        SetupBlockedByStatus(
            UserAccountStatus.Suspended,
            "Suspended",
            "Account suspended.");

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.CheckPasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAccountBlocked_DoesNotGenerateToken()
    {
        SetupUserExists();
        SetupBlockedByStatus(
            UserAccountStatus.Suspended,
            "Suspended",
            "Account suspended.");

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _jwtGenerator.Verify(
            x => x.GenerateToken(
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
                It.IsAny<List<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAccountRestrictedButCanLogin_Proceeds()
    {
        // Restricted users CAN log in but with limited features.
        // The gate should allow them through.
        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Restricted,
            statusLabel: "Restricted",
            statusDescription: "Some features are temporarily limited.",
            canLogin: true,
            canCreateWallets: false,
            canPerformSensitiveOperations: false,
            restrictionReason: "TermsViolation",
            restrictionReasonDetails: "Three warnings in 30 days."));

        SetupNotLockedOut();
        SetupAccountVerified();
        SetupPasswordCheck();
        SetupRoles();
        SetupResetFailedAccess();
        SetupJwtGeneration();
        SetupRefreshTokenCreation();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data?.Data);
        Assert.Null(result.Data!.Blocked);
    }

    [Fact]
    public async Task Handle_WhenRestrictionHasExpiry_BlockedDtoCarriesIt()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(3);

        SetupUserExists();
        SetupBlockedByStatus(
            UserAccountStatus.Restricted,
            "Restricted",
            "Some features are temporarily limited.",
            restrictionReason: "TermsViolation",
            restrictionReasonDetails: "Three warnings in 30 days.",
            restrictionExpiresAt: expiresAt);

        // Override to make sure canLogin is false to trigger the block
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Restricted,
            statusLabel: "Restricted",
            statusDescription: "Some features are temporarily limited.",
            canLogin: false,
            restrictionReason: "TermsViolation",
            restrictionReasonDetails: "Three warnings in 30 days.",
            restrictionExpiresAt: expiresAt));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data?.Blocked);
        Assert.Equal(expiresAt, result.Data!.Blocked!.RestrictionExpiresAt);
    }

    // =========================================================
    // 6. Lockout — already locked at entry
    // =========================================================

    [Fact]
    public async Task Handle_WhenAccountIsLockedOut_ReturnsLocked()
    {
        SetupUserExists();
        SetupPermissions();
        SetupLockedOut(DateTimeOffset.UtcNow.AddMinutes(10));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenAccountIsLockedOut_ReturnsBlockedDtoWithLockoutEnd()
    {
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);

        SetupUserExists();
        SetupPermissions();
        SetupLockedOut(lockoutEnd);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.Blocked);
        Assert.Null(result.Data.Data);
        Assert.Equal(lockoutEnd, result.Data.Blocked!.LockoutEndsAt);
    }

    [Fact]
    public async Task Handle_WhenAccountIsLockedOut_MessageMentionsMinutesRemaining()
    {
        SetupUserExists();
        SetupPermissions();
        SetupLockedOut(DateTimeOffset.UtcNow.AddMinutes(10));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.Contains("Too many failed login attempts", result.Message);
        Assert.Contains("minute", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("locked", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WhenAccountIsLockedOut_DoesNotCheckPassword()
    {
        SetupUserExists();
        SetupPermissions();
        SetupLockedOut(DateTimeOffset.UtcNow.AddMinutes(10));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.CheckPasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAccountIsLockedOut_DoesNotCheckVerification()
    {
        SetupUserExists();
        SetupPermissions();
        SetupLockedOut(DateTimeOffset.UtcNow.AddMinutes(10));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.IsAccountVerifiedAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAccountIsLockedOut_DoesNotGenerateToken()
    {
        SetupUserExists();
        SetupPermissions();
        SetupLockedOut(DateTimeOffset.UtcNow.AddMinutes(10));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _jwtGenerator.Verify(
            x => x.GenerateToken(
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
                It.IsAny<List<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAccountIsLockedOut_DoesNotCreateRefreshToken()
    {
        SetupUserExists();
        SetupPermissions();
        SetupLockedOut(DateTimeOffset.UtcNow.AddMinutes(10));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _refreshTokenService.Verify(
            x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAccountIsLockedOut_DoesNotRecordAnotherFailure()
    {
        SetupUserExists();
        SetupPermissions();
        SetupLockedOut(DateTimeOffset.UtcNow.AddMinutes(10));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.RecordFailedAccessAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================
    // 7. Lockout — wrong password, still under threshold
    // =========================================================

    [Fact]
    public async Task Handle_WhenWrongPassword_RecordsFailedAccess()
    {
        SetupWrongPasswordNotLocked();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.RecordFailedAccessAsync(
                UserId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenWrongPassword_DoesNotResetFailedAccess()
    {
        SetupWrongPasswordNotLocked();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.ResetFailedAccessAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenWrongPassword_DoesNotSaveRefreshToken()
    {
        SetupWrongPasswordNotLocked();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var anyToken = await Context.RefreshTokens.AnyAsync();
        Assert.False(anyToken);
    }

    // =========================================================
    // 8. Lockout — wrong password that triggers the lockout
    // =========================================================

    [Fact]
    public async Task Handle_WhenWrongPasswordTriggersLockout_ReturnsLocked()
    {
        SetupWrongPasswordThatTriggersLockout(DateTimeOffset.UtcNow.AddMinutes(15));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenWrongPasswordTriggersLockout_ReturnsBlockedDtoWithLockoutEnd()
    {
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        SetupWrongPasswordThatTriggersLockout(lockoutEnd);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.Blocked);
        Assert.Equal(lockoutEnd, result.Data.Blocked!.LockoutEndsAt);
    }

    [Fact]
    public async Task Handle_WhenWrongPasswordTriggersLockout_MessageMentionsLockDuration()
    {
        SetupWrongPasswordThatTriggersLockout(DateTimeOffset.UtcNow.AddMinutes(15));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.Contains("Too many failed login attempts", result.Message);
        Assert.Contains("locked", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minute", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_WhenWrongPasswordTriggersLockout_DoesNotCreateRefreshToken()
    {
        SetupWrongPasswordThatTriggersLockout(DateTimeOffset.UtcNow.AddMinutes(15));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _refreshTokenService.Verify(
            x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenWrongPasswordTriggersLockout_DoesNotGenerateToken()
    {
        SetupWrongPasswordThatTriggersLockout(DateTimeOffset.UtcNow.AddMinutes(15));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _jwtGenerator.Verify(
            x => x.GenerateToken(
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
                It.IsAny<List<string>>()),
            Times.Never);
    }

    // =========================================================
    // 9. Reset on success
    // =========================================================

    [Fact]
    public async Task Handle_WhenPasswordValid_ResetsFailedAccessCount()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.ResetFailedAccessAsync(
                UserId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPasswordValid_ResetIsCalledBeforeTokenGeneration()
    {
        SetupHappyPath();

        var callOrder = new List<string>();

        _identityService
            .Setup(x => x.ResetFailedAccessAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("reset"))
            .Returns(Task.CompletedTask);

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
            .Callback(() => callOrder.Add("jwt"))
            .Returns(AccessToken);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(new[] { "reset", "jwt" }, callOrder);
    }

    [Fact]
    public async Task Handle_WhenWrongPassword_DoesNotReset()
    {
        SetupWrongPasswordNotLocked();

        var resetCalled = false;

        _identityService
            .Setup(x => x.ResetFailedAccessAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => resetCalled = true)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.False(resetCalled);
    }

    // =========================================================
    // 10. Response shape — success vs blocked are never mixed
    // =========================================================

    [Fact]
    public async Task Handle_SuccessResponse_NeverCarriesBlockedPayload()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.Data);
        Assert.Null(result.Data.Blocked);
    }

    [Fact]
    public async Task Handle_BlockedResponse_NeverCarriesLoginPayload()
    {
        SetupUserExists();
        SetupBlockedByStatus(
            UserAccountStatus.Suspended,
            "Suspended",
            "Account suspended.");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.Null(result.Data!.Data);
        Assert.NotNull(result.Data.Blocked);
    }
}