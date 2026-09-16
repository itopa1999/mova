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

public sealed class RefreshTokenCommandTests : BaseTest
{
    private const string UserPublicId = "user_refresh_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserPhone = "08050000000";
    private const string IncomingTokenValue = "incoming-refresh-token";
    private const string NewAccessToken = "new.access.jwt";
    private const string NewRefreshTokenValue = "new-refresh-token";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IJwtTokenGenerator> _jwtGenerator = new();
    private readonly Mock<IRefreshTokenService> _refreshTokenService = new();

    private RefreshTokenCommand.Handler CreateHandler()
    {
        return new RefreshTokenCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            _jwtGenerator.Object,
            _refreshTokenService.Object,
            Mock.Of<ILogger<RefreshTokenCommand.Handler>>());
    }

    private RefreshTokenCommand.Command CreateCommand(
        string token = IncomingTokenValue,
        string platform = Platforms.Web)
    {
        return new RefreshTokenCommand.Command
        {
            RefreshToken = token,
            Platform = platform,
        };
    }

    private RefreshToken BuildRefreshToken(
        string publicId = UserPublicId,
        bool expired = false,
        bool revoked = false)
    {
        return new RefreshToken
        {
            UserPublicId = publicId,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiresAt = expired
                ? DateTimeOffset.UtcNow.AddMinutes(-5)
                : DateTimeOffset.UtcNow.AddDays(7),
            CreatedAt = DateTimeOffset.UtcNow,
            RevokedAt = revoked ? DateTimeOffset.UtcNow.AddHours(-1) : null,
            RevocationReason = revoked ? "Old revoke" : null,
        };
    }

    private void SetupValidIncomingToken(RefreshToken? token = null)
    {
        var actual = token ?? BuildRefreshToken();
        await_add_token(actual);

        _refreshTokenService
            .Setup(x => x.ValidateAsync(
                IncomingTokenValue,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(actual);
    }

    private void await_add_token(RefreshToken token)
    {
        UnitOfWork.AddAsync(token).GetAwaiter().GetResult();
        UnitOfWork.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private void SetupInvalidIncomingToken()
    {
        _refreshTokenService
            .Setup(x => x.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);
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
            .Returns(NewAccessToken);
    }

    private void SetupNewTokenCreation()
    {
        _refreshTokenService
            .Setup(x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((NewRefreshTokenValue, new RefreshToken
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
        SetupValidIncomingToken();
        SetupUserExists();
        SetupRoles();
        SetupJwtGeneration();
        SetupNewTokenCreation();
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidToken_ReturnsSuccess()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Token refreshed successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidToken_ReturnsNewTokens()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(NewAccessToken, result.Data!.AccessToken);
        Assert.Equal(NewRefreshTokenValue, result.Data.RefreshToken);
        Assert.Equal(UserPublicId, result.Data.UserPublicId);
        Assert.Equal(UserEmail, result.Data.Email);
        Assert.Equal(Platforms.Web, result.Data.Platform);
        Assert.True(result.Data.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithValidToken_RevokesIncomingToken()
    {
        var incoming = BuildRefreshToken();
        SetupValidIncomingToken(incoming);
        SetupUserExists();
        SetupRoles();
        SetupJwtGeneration();
        SetupNewTokenCreation();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var updated = await Context.RefreshTokens
            .AsNoTracking()
            .FirstAsync(x => x.Id == incoming.Id);

        Assert.NotNull(updated.RevokedAt);
        Assert.Equal("Token refreshed", updated.RevocationReason);
    }

    [Fact]
    public async Task Handle_WithValidToken_PersistsNewRefreshToken()
    {
        SetupHappyPath();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var tokens = await Context.RefreshTokens
            .AsNoTracking()
            .Where(x => x.UserPublicId == UserPublicId)
            .ToListAsync();

        Assert.Equal(2, tokens.Count);
        Assert.Single(tokens, t => t.RevokedAt == null);
        Assert.Single(tokens, t => t.RevokedAt != null);
    }

    [Fact]
    public async Task Handle_WithValidToken_CommitsTransactionOnce()
    {
        SetupHappyPath();

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
            CreateCommand(platform: "UnknownPlatform"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid platform", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithInvalidToken_ReturnsBadRequest()
    {
        SetupInvalidIncomingToken();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid refresh token.", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithExpiredToken_ReturnsBadRequest()
    {
        var expired = BuildRefreshToken(expired: true);
        SetupValidIncomingToken(expired);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("expired", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithRevokedToken_ReturnsBadRequest()
    {
        var revoked = BuildRefreshToken(revoked: true);
        SetupValidIncomingToken(revoked);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("revoked", result.Message);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WhenUserMissing_ReturnsBadRequest()
    {
        SetupValidIncomingToken();
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("User profile not found", result.Message);
    }

    [Fact]
    public async Task Handle_WhenUserMissing_RollsBackTransaction()
    {
        SetupValidIncomingToken();
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    // ---------------------------------------------------------
    // 3. Regression: expired/revoked tokens must not issue new credentials
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithExpiredToken_DoesNotGenerateAccessToken()
    {
        var expired = BuildRefreshToken(expired: true);
        SetupValidIncomingToken(expired);

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
    public async Task Handle_WithRevokedToken_DoesNotCreateNewToken()
    {
        var revoked = BuildRefreshToken(revoked: true);
        SetupValidIncomingToken(revoked);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _refreshTokenService.Verify(
            x => x.CreateAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidToken_DoesNotPersistAnything()
    {
        SetupInvalidIncomingToken();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var anyToken = await Context.RefreshTokens.AnyAsync();
        Assert.False(anyToken);
    }

    // ---------------------------------------------------------
    // 4. Regression: incoming token revocation
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenSaveChangesThrows_RollsBackIncomingTokenRevocation()
    {
        var incoming = BuildRefreshToken();
        SetupValidIncomingToken(incoming);
        SetupUserExists();
        SetupRoles();
        SetupJwtGeneration();
        SetupNewTokenCreation();

        var failingUow = new Mock<Mova.Application.Interfaces.Persistence.IUnitOfWork>();

        failingUow
            .Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        failingUow
            .Setup(x => x.Update(It.IsAny<RefreshToken>()))
            .Callback<RefreshToken>(t =>
            {
                incoming.RevokedAt = t.RevokedAt;
                incoming.RevocationReason = t.RevocationReason;
            });

        failingUow
            .Setup(x => x.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        failingUow
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated failure"));

        failingUow
            .Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new RefreshTokenCommand.Handler(
            _identityService.Object,
            failingUow.Object,
            _jwtGenerator.Object,
            _refreshTokenService.Object,
            Mock.Of<ILogger<RefreshTokenCommand.Handler>>());

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await handler.Handle(CreateCommand(), default));

        failingUow.Verify(
            x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}