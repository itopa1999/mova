using System.Net;
using Moq;
using Mova.Application.BBL.Queries.Profile;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class GetProfileTests : BaseTest
{
    private const string UserPublicId = "user_profile_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";
    private const string UserLastName = "Starboy";
    private const string UserFullName = "Lucky Starboy";
    private const string UserPhone = "08050000000";

    private readonly Mock<IIdentityService> _identityService = new();

    private GetProfile.Handler CreateHandler()
    {
        return new GetProfile.Handler(_identityService.Object);
    }

    private GetProfile.Query CreateQuery(string? userPublicId = null)
    {
        return new GetProfile.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
        };
    }

    // ---------------------------------------------------------
    // Mock setups
    // ---------------------------------------------------------

    private void SetupUserExists(
        decimal balanceNaira = 0m,
        string? publicId = null,
        string? email = null,
        string? firstName = null,
        string? lastName = null,
        string? otherNames = null,
        string? fullName = null,
        string? phone = null,
        string? profilePicture = null,
        string? transactionPinHash = null,
        bool notifyLoginAlerts = true,
        bool notifyReleaseAlerts = true,
        bool notifyProductUpdates = true,
        bool notifyPromotions = false,
        DateTimeOffset? createdAt = null)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                UserId,
                publicId ?? UserPublicId,
                firstName ?? UserFirstName,
                otherNames,
                lastName ?? UserLastName,
                email ?? UserEmail,
                phone ?? UserPhone,
                profilePicture,
                Money.FromNaira(balanceNaira),
                transactionPinHash ?? string.Empty,
                notifyLoginAlerts,
                notifyReleaseAlerts,
                notifyProductUpdates,
                notifyPromotions,
                fullName ?? UserFullName,
                createdAt ?? DateTimeOffset.UtcNow));
    }

    private void SetupUserNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);
    }

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

    // =========================================================
    // 1. Validation
    // =========================================================

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(userPublicId: ""),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User ID is required.", result.Message);
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
        Assert.Equal("User ID is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_DoesNotLookupUser()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateQuery(userPublicId: ""), default);

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
        await handler.Handle(CreateQuery(userPublicId: ""), default);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =========================================================
    // 2. User lookup
    // =========================================================

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsNotFound()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_DoesNotCheckPermissions()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(), default);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_LooksUpUserExactlyOnce()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(), default);

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================
    // 3. Personal field mapping
    // =========================================================

    [Fact]
    public async Task Handle_MapsAllPersonalFields()
    {
        SetupUserExists(
            firstName: "Amaka",
            lastName: "Okafor",
            otherNames: "Chinwe",
            fullName: "Amaka Chinwe Okafor",
            email: "amaka@mova.app",
            phone: "+2348012345678",
            profilePicture: "https://cdn.mova.app/amaka.png");
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        var dto = result.Data!;
        Assert.Equal("Amaka", dto.FirstName);
        Assert.Equal("Okafor", dto.LastName);
        Assert.Equal("Chinwe", dto.OtherName);
        Assert.Equal("Amaka Chinwe Okafor", dto.FullName);
        Assert.Equal("amaka@mova.app", dto.Email);
        Assert.Equal("+2348012345678", dto.Phone);
        Assert.Equal("https://cdn.mova.app/amaka.png", dto.ProfilePicture);
    }

    [Fact]
    public async Task Handle_WhenProfilePictureMissing_ReturnsNull()
    {
        SetupUserExists(profilePicture: null);
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Null(result.Data!.ProfilePicture);
    }

    [Fact]
    public async Task Handle_ReturnsBalanceAsDecimal()
    {
        SetupUserExists(balanceNaira: 25_750.50m);
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(25_750.50m, result.Data!.Balance);
    }

    [Fact]
    public async Task Handle_ReturnsCreatedAt()
    {
        var createdAt = new DateTimeOffset(
            2024, 6, 15, 10, 30, 0, TimeSpan.Zero);

        SetupUserExists(createdAt: createdAt);
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(createdAt, result.Data!.CreatedAt);
    }

    // =========================================================
    // 4. Notification preferences
    // =========================================================

    [Fact]
    public async Task Handle_MapsNotificationPreferences_AllEnabled()
    {
        SetupUserExists(
            notifyLoginAlerts: true,
            notifyReleaseAlerts: true,
            notifyProductUpdates: true,
            notifyPromotions: true);
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var notifications = result.Data!.Notifications;
        Assert.True(notifications.Login);
        Assert.True(notifications.Release);
        Assert.True(notifications.Updates);
        Assert.True(notifications.Promotions);
    }

    [Fact]
    public async Task Handle_MapsNotificationPreferences_AllDisabled()
    {
        SetupUserExists(
            notifyLoginAlerts: false,
            notifyReleaseAlerts: false,
            notifyProductUpdates: false,
            notifyPromotions: false);
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var notifications = result.Data!.Notifications;
        Assert.False(notifications.Login);
        Assert.False(notifications.Release);
        Assert.False(notifications.Updates);
        Assert.False(notifications.Promotions);
    }

    [Fact]
    public async Task Handle_MapsNotificationPreferences_MixedValues()
    {
        SetupUserExists(
            notifyLoginAlerts: true,
            notifyReleaseAlerts: false,
            notifyProductUpdates: true,
            notifyPromotions: false);
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var notifications = result.Data!.Notifications;
        Assert.True(notifications.Login);
        Assert.False(notifications.Release);
        Assert.True(notifications.Updates);
        Assert.False(notifications.Promotions);
    }

    // =========================================================
    // 5. PIN status
    // =========================================================

    [Fact]
    public async Task Handle_WithPinHashSet_HasPinSetIsTrue()
    {
        SetupUserExists(transactionPinHash: "hashed-pin-value");
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.Data!.HasPinSet);
    }

    [Fact]
    public async Task Handle_WithEmptyPinHash_HasPinSetIsFalse()
    {
        SetupUserExists(transactionPinHash: "");
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.Data!.HasPinSet);
    }

    [Fact]
    public async Task Handle_WithWhitespacePinHash_HasPinSetIsFalse()
    {
        SetupUserExists(transactionPinHash: "   ");
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.Data!.HasPinSet);
    }

    // =========================================================
    // 6. Account status
    // =========================================================

    [Fact]
    public async Task Handle_AlwaysIncludesAccountStatus()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.AccountStatus);
    }

    [Fact]
    public async Task Handle_WithActiveAccount_StatusIsHealthy()
    {
        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Active,
            statusLabel: "Active",
            statusDescription: "Your account is in good standing and fully operational.",
            restrictionExpiresAt: null));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Active", status.Status);
        Assert.Equal("Active", status.StatusLabel);
        Assert.True(status.IsHealthy);
        Assert.Null(status.RestrictionReason);
        Assert.Null(status.RestrictionReasonDetails);
        Assert.Null(status.RestrictionExpiresAt);
    }

    [Fact]
    public async Task Handle_WithRestrictedAccount_IsNotHealthy()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Restricted,
            statusLabel: "Restricted",
            statusDescription: "Some features are temporarily limited.",
            canCreateWallets: false,
            canPerformSensitiveOperations: false,
            restrictionReason: "TermsViolation",
            restrictionReasonDetails: "Three warnings in 30 days.",
            restrictionExpiresAt: expiresAt));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Restricted", status.Status);
        Assert.Equal("Restricted", status.StatusLabel);
        Assert.False(status.IsHealthy);
        Assert.Equal("TermsViolation", status.RestrictionReason);
        Assert.Equal("Three warnings in 30 days.", status.RestrictionReasonDetails);
        Assert.Equal(expiresAt, status.RestrictionExpiresAt);
    }

    [Fact]
    public async Task Handle_WithSuspendedAccount_IsNotHealthy()
    {
        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Suspended,
            statusLabel: "Suspended",
            statusDescription: "Your account is suspended.",
            canLogin: false,
            canCreateWallets: false,
            canPerformSensitiveOperations: false,
            canReceivePayouts: false,
            restrictionReason: "SuspectedFraud",
            restrictionReasonDetails: "Chargebacks from 4 cards."));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Suspended", status.Status);
        Assert.False(status.IsHealthy);
        Assert.Equal("SuspectedFraud", status.RestrictionReason);
    }

    [Fact]
    public async Task Handle_WithClosedAccount_IsNotHealthy()
    {
        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Closed,
            statusLabel: "Closed",
            statusDescription: "Your account is permanently closed.",
            canLogin: false,
            canCreateWallets: false,
            canPerformSensitiveOperations: false,
            canReceivePayouts: false,
            restrictionReason: "UserRequestedDeactivation"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Closed", status.Status);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WithDeactivatedAccount_IsNotHealthy()
    {
        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Deactivated,
            statusLabel: "Deactivated",
            statusDescription: "Your account is deactivated.",
            canLogin: false,
            canCreateWallets: false,
            canPerformSensitiveOperations: false));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Deactivated", status.Status);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WithActiveStatusButExpiredRestriction_IsNotHealthy()
    {
        // Edge case: status says Active but restriction expiry is in the past.
        // The FE should still see the banner, so IsHealthy must be false.
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        SetupUserExists();
        SetupPermissions(BuildPermissions(
            status: UserAccountStatus.Active,
            statusLabel: "Active",
            statusDescription: "Your account is in good standing.",
            restrictionExpiresAt: expiredAt));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Active", status.Status);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_ReturnsUnavailableStatus()
    {
        SetupUserExists();
        SetupPermissionsReturnsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data?.AccountStatus);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Unknown", status.Status);
        Assert.Equal("Unavailable", status.StatusLabel);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WhenPermissionsUnavailable_StillReturnsProfileData()
    {
        // Fail-safe: the FE still needs the profile even if the account
        // status couldn't be resolved.
        SetupUserExists(balanceNaira: 1_000m);
        SetupPermissionsReturnsNull();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(1_000m, result.Data!.Balance);
        Assert.False(result.Data.AccountStatus.IsHealthy);
    }

    [Fact]
    public async Task Handle_ChecksPermissionsExactlyOnce()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        await handler.Handle(CreateQuery(), default);

        _identityService.Verify(
            x => x.GetPermissionsAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================
    // 7. Response envelope
    // =========================================================

    [Fact]
    public async Task Handle_OnSuccess_ReturnsOk()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_OnSuccess_ReturnsSuccessMessage()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal("Profile retrieved successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_OnSuccess_ReturnsNonNullData()
    {
        SetupUserExists();
        SetupPermissions();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.Notifications);
        Assert.NotNull(result.Data.AccountStatus);
    }
}