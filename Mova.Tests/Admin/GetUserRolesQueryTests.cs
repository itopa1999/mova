using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Queries.Admin;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.ValueObjects;
using Mova.Shared.Constants;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class GetUserRolesQueryTests
{
    private const long UserId = 42;
    private const string UserPublicId = "0001";

    private readonly Mock<IIdentityService> _identityService = new();

    private GetUserRolesQuery.Handler CreateHandler()
    {
        return new GetUserRolesQuery.Handler(
            _identityService.Object,
            Mock.Of<ILogger<GetUserRolesQuery.Handler>>());
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

    private static GetUserRolesQuery.Query CreateQuery(
        string userPublicId = UserPublicId)
    {
        return new GetUserRolesQuery.Query
        {
            UserPublicId = userPublicId,
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

    // ─────────────────────────────────────────────────────────
    // 1. Happy path
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithValidUser_ReturnsRolesAndOk()
    {
        SetupUserExists();
        SetupRoles(Roles.Customer, Roles.Admin);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
        Assert.Equal(2, result.Data.Roles.Count);
        Assert.Contains(Roles.Customer, result.Data.Roles);
        Assert.Contains(Roles.Admin, result.Data.Roles);
    }

    [Fact]
    public async Task Handle_WithUserHavingSingleRole_ReturnsThatRole()
    {
        SetupUserExists();
        SetupRoles(Roles.Customer);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data!.Roles);
        Assert.Equal(Roles.Customer, result.Data.Roles[0]);
    }

    [Fact]
    public async Task Handle_WithUserHavingNoRoles_ReturnsEmptyListAndOk()
    {
        SetupUserExists();
        SetupRoles(); // no roles

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!.Roles);
    }

    [Fact]
    public async Task Handle_WithUserHavingAllRoles_ReturnsAllRoles()
    {
        SetupUserExists();
        SetupRoles(
            Roles.Customer,
            Roles.Admin,
            Roles.SuperAdmin,
            Roles.SupportAgent);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(4, result.Data!.Roles.Count);
    }

    // ─────────────────────────────────────────────────────────
    // 2. Validation failures
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(userPublicId: string.Empty),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithWhitespaceUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(userPublicId: "   "),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithInvalidUserPublicId_DoesNotCallIdentityService()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateQuery(userPublicId: string.Empty), default);

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _identityService.Verify(
            x => x.GetRolesAsync(
                It.IsAny<long>(),
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
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_DoesNotCallGetRoles()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(), default);

        _identityService.Verify(
            x => x.GetRolesAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_MessageMentionsNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────
    // 4. Correct UserId passed to GetRolesAsync
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PassesInternalUserIdToGetRoles()
    {
        var user = BuildUser(id: 999, publicId: UserPublicId);

        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _identityService
            .Setup(x => x.GetRolesAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Admin });

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(), default);

        _identityService.Verify(
            x => x.GetRolesAsync(999, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────
    // 5. Response shape
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_OnSuccess_ReturnsRequestedUserPublicId()
    {
        SetupUserExists();
        SetupRoles(Roles.Admin);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.NotNull(result.Data);
        Assert.Equal(UserPublicId, result.Data!.UserPublicId);
    }

    [Fact]
    public async Task Handle_OnSuccess_MessageMentionsRetrieval()
    {
        SetupUserExists();
        SetupRoles(Roles.Customer);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task Handle_OnSuccess_RolesAreReadOnlyList()
    {
        SetupUserExists();
        SetupRoles(Roles.Admin, Roles.Customer);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.NotNull(result.Data);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(result.Data!.Roles);
    }

    // ─────────────────────────────────────────────────────────
    // 6. Query passes the exact identifier through
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithDifferentIdentifier_PassesItToIdentityService()
    {
        const string otherPublicId = "0042";
        var user = BuildUser(id: 500, publicId: otherPublicId);

        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                otherPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _identityService
            .Setup(x => x.GetRolesAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { Roles.Customer });

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(userPublicId: otherPublicId),
            default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(otherPublicId, result.Data!.UserPublicId);
    }

    // ─────────────────────────────────────────────────────────
    // 7. Idempotency — calling twice returns same result
    // ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CalledTwice_ReturnsSameRolesBothTimes()
    {
        SetupUserExists();
        SetupRoles(Roles.Admin, Roles.Customer);

        var handler = CreateHandler();
        var first = await handler.Handle(CreateQuery(), default);
        var second = await handler.Handle(CreateQuery(), default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotNull(first.Data);
        Assert.NotNull(second.Data);
        Assert.Equal(first.Data!.Roles, second.Data!.Roles);
    }
}