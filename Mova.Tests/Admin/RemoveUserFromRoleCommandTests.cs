using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Admin;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class RemoveUserFromRoleCommandTests
{
    private const long UserId = 42;
    private const string UserPublicId = "0001";

    private readonly Mock<IIdentityService> _identityService = new();

    private RemoveUserFromRoleCommand.Handler CreateHandler()
    {
        return new RemoveUserFromRoleCommand.Handler(
            _identityService.Object,
            Mock.Of<ILogger<RemoveUserFromRoleCommand.Handler>>());
    }

    private static UserIdentityDto BuildUser(
        long id = UserId,
        string publicId = UserPublicId)
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

    private static RemoveUserFromRoleCommand.Command CreateCommand(
        string userPublicId = UserPublicId,
        string role = Roles.Admin)
    {
        return new RemoveUserFromRoleCommand.Command
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

    private void SetupRemoveSucceeds()
    {
        _identityService
            .Setup(x => x.RemoveFromRoleAsync(
                UserId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
    }

    private void SetupSuperAdminCount(int count)
    {
        _identityService
            .Setup(x => x.CountUsersInRoleAsync(
                Roles.SuperAdmin,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);
    }

    // ─────────────────────────────────────────────────────────
    // 1. Happy path
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithValidRequest_RemovesRoleAndReturnsOk()
    {
        SetupUserExists();
        SetupSuperAdminCount(5);
        SetupRemoveSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Customer, Roles.Admin })
            .ReturnsAsync(new List<string> { Roles.Customer });

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
        Assert.Contains(Roles.Customer, result.Data.Roles);
        Assert.DoesNotContain(Roles.Admin, result.Data.Roles);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsRemoveFromRoleExactlyOnce()
    {
        SetupUserExists();
        SetupSuperAdminCount(5);
        SetupRemoveSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Customer, Roles.Admin })
            .ReturnsAsync(new List<string> { Roles.Customer });

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(role: Roles.Admin), default);

        _identityService.Verify(
            x => x.RemoveFromRoleAsync(
                UserId,
                Roles.Admin,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SupportAgent)]
    [InlineData(Roles.Customer)]
    public async Task Handle_WithEachNonSuperAdminRole_Succeeds(string role)
    {
        SetupUserExists();
        SetupSuperAdminCount(5);
        SetupRemoveSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { role })
            .ReturnsAsync(new List<string>());

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
    public async Task Handle_WithWhitespaceRole_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(role: "   "),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
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
    public async Task Handle_WhenUserNotFound_DoesNotRemoveRole()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.RemoveFromRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────
    // 4. Idempotency — user doesn't have the role
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenUserDoesNotHaveRole_ReturnsOkWithoutRemoving()
    {
        SetupUserExists();
        SetupRoles(Roles.Customer);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("does not have", result.Message);

        _identityService.Verify(
            x => x.RemoveFromRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotHaveRole_ReturnsExistingRoles()
    {
        SetupUserExists();
        SetupRoles(Roles.Customer, Roles.SupportAgent);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data!.Roles.Count);
        Assert.Contains(Roles.Customer, result.Data.Roles);
        Assert.Contains(Roles.SupportAgent, result.Data.Roles);
    }

    [Fact]
    public async Task Handle_WhenRoleDiffersOnlyByCasing_StillRemovesRole()
    {
        SetupUserExists();
        SetupSuperAdminCount(5);
        SetupRemoveSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "ADMIN", Roles.Customer })   // before
            .ReturnsAsync(new List<string> { Roles.Customer });            // after

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: "admin"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        // The case-insensitive check matched, so removal DID happen
        _identityService.Verify(
            x => x.RemoveFromRoleAsync(
                UserId,
                "admin",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    // ─────────────────────────────────────────────────────────
    // 5. Last SuperAdmin protection
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenRemovingLastSuperAdmin_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupRoles(Roles.SuperAdmin);
        SetupSuperAdminCount(1);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(role: Roles.SuperAdmin),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("last SuperAdmin", result.Message);
    }

    [Fact]
    public async Task Handle_WhenRemovingLastSuperAdmin_DoesNotCallRemove()
    {
        SetupUserExists();
        SetupRoles(Roles.SuperAdmin);
        SetupSuperAdminCount(1);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(role: Roles.SuperAdmin), default);

        _identityService.Verify(
            x => x.RemoveFromRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRemovingSuperAdminWithOthersPresent_Succeeds()
    {
        SetupUserExists();
        SetupSuperAdminCount(3);
        SetupRemoveSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.SuperAdmin, Roles.Customer })
            .ReturnsAsync(new List<string> { Roles.Customer });

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(role: Roles.SuperAdmin),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenRemovingSuperAdminWithExactlyTwo_AllowsRemoval()
    {
        SetupUserExists();
        SetupSuperAdminCount(2);   // boundary: 2 > 1, so allowed
        SetupRemoveSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.SuperAdmin, Roles.Customer })
            .ReturnsAsync(new List<string> { Roles.Customer });

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(role: Roles.SuperAdmin),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenRemovingLastSuperAdminDifferentCasing_StillBlocks()
    {
        SetupUserExists();
        SetupRoles("SUPERADMIN");
        SetupSuperAdminCount(1);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(role: "superadmin"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("last SuperAdmin", result.Message);
    }

    [Fact]
    public async Task Handle_WhenRemovingNonSuperAdminRole_DoesNotCheckSuperAdminCount()
    {
        SetupUserExists();
        SetupSuperAdminCount(1);   // Would block SuperAdmin removal
        SetupRemoveSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Customer, Roles.Admin })
            .ReturnsAsync(new List<string> { Roles.Customer });

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        // Should succeed despite only 1 SuperAdmin in the system
        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    // ─────────────────────────────────────────────────────────
    // 6. RemoveFromRoleAsync failure
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenRemoveFails_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupSuperAdminCount(5);
        SetupRoles(Roles.Admin, Roles.Customer);

        _identityService
            .Setup(x => x.RemoveFromRoleAsync(
                UserId,
                Roles.Admin,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Role store unavailable."));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Role store unavailable", result.Message);
    }

    // ─────────────────────────────────────────────────────────
    // 7. Correct UserId passed (not PublicId)
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PassesInternalUserIdToRemoveFromRole()
    {
        var user = BuildUser(id: 777, publicId: UserPublicId);
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _identityService
            .Setup(x => x.GetRolesAsync(777, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Admin, Roles.Customer });

        _identityService
            .Setup(x => x.CountUsersInRoleAsync(
                Roles.SuperAdmin,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        _identityService
            .Setup(x => x.RemoveFromRoleAsync(
                777,
                Roles.Admin,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(role: Roles.Admin), default);

        _identityService.Verify(
            x => x.RemoveFromRoleAsync(
                777,
                Roles.Admin,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────
    // 8. Response shape
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_OnSuccess_ReturnsRequestedUserPublicIdInResponse()
    {
        SetupUserExists();
        SetupSuperAdminCount(5);
        SetupRemoveSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Admin, Roles.Customer })
            .ReturnsAsync(new List<string> { Roles.Customer });

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
    }

    [Fact]
    public async Task Handle_OnSuccess_MessageMentionsRoleName()
    {
        SetupUserExists();
        SetupSuperAdminCount(5);
        SetupRemoveSucceeds();

        _identityService
            .SetupSequence(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.SupportAgent, Roles.Customer })
            .ReturnsAsync(new List<string> { Roles.Customer });

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(role: Roles.SupportAgent),
            default);

        Assert.Contains(Roles.SupportAgent, result.Message);
    }

    // ─────────────────────────────────────────────────────────
    // 9. Idempotency — calling twice with same inputs is safe
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenRoleAlreadyRemoved_IsIdempotent()
    {
        SetupUserExists();
        SetupRoles(Roles.Customer);   // user only has Customer — no Admin to remove

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(role: Roles.Admin), default);

        // Second attempt at "remove Admin" — nothing to do, returns 200
        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        _identityService.Verify(
            x => x.RemoveFromRoleAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}