using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;
using Xunit;

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

    private void SetupAccountVerified(bool verified = true)
    {
        _identityService
            .Setup(x => x.IsAccountVerifiedAsync(It.IsAny<long>()))
            .ReturnsAsync(verified);
    }

    private void SetupPasswordCheck(bool valid = true)
    {
        _identityService
            .Setup(x => x.CheckPasswordAsync(
                It.IsAny<long>(),
                It.IsAny<string>()))
            .ReturnsAsync(valid);
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

    private void SetupHappyPath()
    {
        SetupUserExists();
        SetupAccountVerified();
        SetupPasswordCheck();
        SetupRoles();
        SetupJwtGeneration();
        SetupRefreshTokenCreation();
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsSuccess()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Login successful.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsAllUserFields()
    {
        SetupUserExists(profilePicture: "https://example.com/avatar.png");
        SetupAccountVerified();
        SetupPasswordCheck();
        SetupRoles("User");
        SetupJwtGeneration();
        SetupRefreshTokenCreation();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
        Assert.Equal(UserEmail, result.Data.Email);
        Assert.Equal(UserPhone, result.Data.Phone);
        Assert.Equal("Lucky Starboy", result.Data.FullName);
        Assert.Equal("https://example.com/avatar.png", result.Data.ProfilePicture);
        Assert.Equal(Platforms.Web, result.Data.Platform);
        Assert.Equal(AccessToken, result.Data.AccessToken);
        Assert.Equal(RefreshTokenValue, result.Data.RefreshToken);
        Assert.True(result.Data.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
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
        Assert.Equal(Platforms.Mobile, result.Data!.Platform);
    }

    // ---------------------------------------------------------
    // 2. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithInvalidPlatform_ReturnsBadRequest()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(platform: "UnknownPlatform"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid platform", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsBadRequestWithGenericMessage()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid email or password.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnverifiedAccount_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupAccountVerified(verified: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("verify your account", result.Message);
    }

    [Fact]
    public async Task Handle_WithWrongPassword_ReturnsBadRequestWithGenericMessage()
    {
        SetupUserExists();
        SetupAccountVerified();
        SetupPasswordCheck(valid: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid email or password.", result.Message);
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
        SetupUserExists();
        SetupAccountVerified();
        SetupPasswordCheck(valid: false);

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

    // ---------------------------------------------------------
    // 3. Security: generic error messages do not leak account existence
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_UnknownUserAndWrongPassword_ReturnIdenticalMessages()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var unknownUserResult = await handler.Handle(CreateCommand(), default);

        ResetDatabase();

        _identityService.Reset();
        SetupUserExists();
        SetupAccountVerified();
        SetupPasswordCheck(valid: false);

        var wrongPasswordResult = await handler.Handle(CreateCommand(), default);

        Assert.Equal(unknownUserResult.Message, wrongPasswordResult.Message);
        Assert.Equal(unknownUserResult.StatusCode, wrongPasswordResult.StatusCode);
    }

    // ---------------------------------------------------------
    // 4. Regression: token persistence
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidCredentials_PersistsRefreshTokenOnce()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }
}