using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Admin;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class AddUserToRoleCommandTests
{
    private const long UserId = 42;
    private const string UserPublicId = "0001";

    private readonly Mock<IIdentityService> _identityService = new();

    private AddUserToRoleCommand.Handler CreateHandler()
    {
        return new AddUserToRoleCommand.Handler(
            _identityService.Object,
            Mock.Of<ILogger<AddUserToRoleCommand.Handler>>());
    }

    private static UserIdentityDto BuildUser(long id = UserId, string publicId = UserPublicId)
    {
        return new UserIdentityDto(
            id,
            publicId,
            "Salawu",
            "Other",
            "Lucky",
            "user@example.com",
            "+2348000000000",
            "https://example.com/avatar.png",
            Money.FromNaira(0),
            string.Empty,
            true,
            true,
            true,
            true,
            string.Empty,
            DateTimeOffset.UtcNow);
    }

    private static AddUserToRoleCommand.Command CreateCommand(
        string userPublicId = UserPublicId,
        string role = Roles.Admin)
    {
        return new AddUserToRoleCommand.Command
        {
            UserPublicId = userPublicId,
            Role = role,
        };
    }

    private void SetupUserExists(UserIdentityDto? user = null)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user ?? BuildUser());
    }

    private void SetupRoles(params string[] roles)
    {
        _identityService
            .Setup(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles.ToList());
    }

    private void SetupAddSucceeds()
    {
        _identityService
            .Setup(x => x.AddToRoleAsync(
                UserId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
    }

    // ─────────────────────────────────────────────────────────
    // 1. Happy path
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithValidRequest_AddsRoleAndReturnsOk()
    {
        SetupUserExists();
        SetupRoles(Roles.Customer);
        SetupAddSucceeds();

        // After adding, return updated roles
        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Customer })
            .ReturnsAsync(new List<string> { Roles.Customer, Roles.Admin });

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
        Assert.Contains(Roles.Admin, result.Data.Roles);
        Assert.Contains(Roles.Customer, result.Data.Roles);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsAddToRoleExactlyOnce()
    {
        SetupUserExists();
        SetupAddSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Customer })
            .ReturnsAsync(new List<string> { Roles.Customer, Roles.Admin });

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(role: Roles.Admin), default);

        _identityService.Verify(
            x => x.AddToRoleAsync(
                UserId,
                Roles.Admin,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    [InlineData(Roles.SupportAgent)]
    [InlineData(Roles.Customer)]
    public async Task Handle_WithEachAllowedRole_Succeeds(string role)
    {
        SetupUserExists();
        SetupAddSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>())
            .ReturnsAsync(new List<string> { role });

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: role), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    // ─────────────────────────────────────────────────────────
    // 2. Validation failures
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(userPublicId: string.Empty),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithWhitespaceUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(userPublicId: "   "),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithEmptyRole_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(role: string.Empty),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithUnknownRole_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(role: "Wizard"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Unknown role", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownRole_DoesNotCallIdentityService()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(role: "Wizard"), default);

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _identityService.Verify(
            x => x.AddToRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────
    // 3. User not found
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_DoesNotAddRole()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.AddToRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────
    // 4. Idempotency — user already has role
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenUserAlreadyHasRole_ReturnsOkWithoutAdding()
    {
        SetupUserExists();
        SetupRoles(Roles.Admin, Roles.Customer);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("already has", result.Message);

        _identityService.Verify(
            x => x.AddToRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserAlreadyHasRole_ReturnsExistingRoles()
    {
        SetupUserExists();
        SetupRoles(Roles.Admin, Roles.Customer);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data!.Roles.Count);
        Assert.Contains(Roles.Admin, result.Data.Roles);
        Assert.Contains(Roles.Customer, result.Data.Roles);
    }

    [Fact]
    public async Task Handle_WhenUserAlreadyHasRoleDifferentCasing_IsIdempotent()
    {
        SetupUserExists();
        SetupRoles("ADMIN", Roles.Customer);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: "admin"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("already has", result.Message);

        _identityService.Verify(
            x => x.AddToRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────
    // 5. Case-insensitive role validation
    // ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    [InlineData("aDmIn")]
    public async Task Handle_WithRoleDifferentCasing_AcceptsRole(string role)
    {
        SetupUserExists();
        SetupAddSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>())
            .ReturnsAsync(new List<string> { role });

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: role), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    // ─────────────────────────────────────────────────────────
    // 6. AddToRoleAsync failure
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenAddToRoleFails_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupRoles(Roles.Customer);

        _identityService
            .Setup(x => x.AddToRoleAsync(
                UserId,
                Roles.Admin,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Role does not exist in the store."));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Role does not exist", result.Message);
    }

    [Fact]
    public async Task Handle_WhenAddToRoleFailsWithEmptyMessage_ReturnsGenericError()
    {
        SetupUserExists();
        SetupRoles(Roles.Customer);

        _identityService
            .Setup(x => x.AddToRoleAsync(
                UserId,
                Roles.Admin,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, string.Empty));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    // ─────────────────────────────────────────────────────────
    // 7. Passes correct UserId (not PublicId) to identity service
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PassesInternalUserIdToAddToRole()
    {
        var user = BuildUser(id: 999, publicId: UserPublicId);
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _identityService
            .Setup(x => x.GetRolesAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Customer });

        _identityService
            .Setup(x => x.AddToRoleAsync(999, Roles.Admin, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(role: Roles.Admin), default);

        _identityService.Verify(
            x => x.AddToRoleAsync(
                999,
                Roles.Admin,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────
    // 8. Response DTO shape
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_OnSuccess_ReturnsRequestedUserPublicIdInResponse()
    {
        SetupUserExists();
        SetupAddSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>())
            .ReturnsAsync(new List<string> { Roles.Admin });

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
    }

    [Fact]
    public async Task Handle_OnSuccess_MessageMentionsRoleName()
    {
        SetupUserExists();
        SetupAddSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>())
            .ReturnsAsync(new List<string> { Roles.SuperAdmin });

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(role: Roles.SuperAdmin),
            default);

        Assert.Contains(Roles.SuperAdmin, result.Message);
    }
}