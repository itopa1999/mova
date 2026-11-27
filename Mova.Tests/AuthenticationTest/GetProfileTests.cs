using System.Net;
using Microsoft.EntityFrameworkCore;
using Mova.Application.BBL.Queries.Profile;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Tests.AuthenticationTest;

public sealed class GetProfileTests : BaseTest
{
    private const string UserPublicId = "user_profile_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserPhone = "08050000000";

    private async Task<User> SeedProfileUserAsync(
        string? publicId = null,
        long? userId = null,
        string? email = null,
        string? firstName = null,
        string? lastName = null,
        string? otherNames = null,
        string? phone = null,
        string? profilePicture = null,
        decimal balanceNaira = 0m,
        string? transactionPinHash = null,
        UserAccountStatus accountStatus = UserAccountStatus.Active,
        AccountRestrictionReason restrictionReason = AccountRestrictionReason.None,
        string? restrictionReasonDetails = null,
        DateTimeOffset? restrictionExpiresAt = null,
        bool notifyLoginAlerts = true,
        bool notifyReleaseAlerts = true,
        bool notifyProductUpdates = true,
        bool notifyPromotions = false,
        DateTimeOffset? createdAt = null)
    {
        var user = new User
        {
            UserId = userId ?? UserId,
            PublicId = publicId ?? UserPublicId,
            Email = email ?? UserEmail,
            EmailConfirmed = true,
            FirstName = firstName ?? "Lucky",
            LastName = lastName ?? "Starboy",
            OtherNames = otherNames,
            PhoneNumber = phone ?? UserPhone,
            PhoneNumberConfirmed = true,
            ProfilePicture = profilePicture,
            Balance = Money.FromNaira(balanceNaira),
            TransactionPinHash = transactionPinHash,
            AccountStatus = accountStatus,
            RestrictionReason = restrictionReason,
            RestrictionReasonDetails = restrictionReasonDetails,
            RestrictionExpiresAt = restrictionExpiresAt,
            NotifyLoginAlerts = notifyLoginAlerts,
            NotifyReleaseAlerts = notifyReleaseAlerts,
            NotifyProductUpdates = notifyProductUpdates,
            NotifyPromotions = notifyPromotions,
            OnboardingStage = OnboardingStage.Completed,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            CreatedBy = "test",
            IsDeleted = false,
        };

        await UnitOfWork.AddAsync(user);
        await UnitOfWork.SaveChangesAsync();
        UnitOfWork.ResetCounts();

        return user;
    }

    private async Task<RequestProfileChange> SeedPendingChangeAsync(
        string? userPublicId = null,
        bool isApproved = false,
        bool isRejected = false,
        string requestReason = "Name change",
        string? requestComment = "Marriage",
        string? rejectionReason = null,
        DateTimeOffset? rejectedAt = null,
        DateTimeOffset? createdAt = null)
    {
        var change = new RequestProfileChange
        {
            UserPublicId = userPublicId ?? UserPublicId,
            IsApproved = isApproved,
            IsRejected = isRejected,
            RequestReason = requestReason,
            RequestComment = requestComment,
            RejectionReason = rejectionReason,
            RejectedAt = rejectedAt,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

        await UnitOfWork.AddAsync(change);
        await UnitOfWork.SaveChangesAsync();
        UnitOfWork.ResetCounts();

        return change;
    }

    private GetProfile.Handler CreateHandler() => new(UnitOfWork);

    private static GetProfile.Query CreateQuery(string? userPublicId = null) =>
        new() { UserPublicId = userPublicId ?? UserPublicId };

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var result = await CreateHandler().Handle(CreateQuery(""), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User ID is required.", result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Handle_WithWhitespaceUserPublicId_ReturnsBadRequest()
    {
        var result = await CreateHandler().Handle(CreateQuery("   "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User ID is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsFailure()
    {
        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Handle_WithSoftDeletedUser_StillReturnsProfile()
    {
        await SeedProfileUserAsync();
        var user = await UnitOfWork.Query<User>().FirstAsync();
        user.IsDeleted = true;
        await UnitOfWork.SaveChangesAsync();
        UnitOfWork.ResetCounts();

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_OnSuccess_ReturnsOk()
    {
        await SeedProfileUserAsync();

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Profile retrieved successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_OnSuccess_ReturnsNonNullData()
    {
        await SeedProfileUserAsync();

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.NotNull(result.Data);
        Assert.NotNull(result.Data!.Notifications);
        Assert.NotNull(result.Data.AccountStatus);
    }

    [Fact]
    public async Task Handle_MapsAllPersonalFields()
    {
        await SeedProfileUserAsync(
            firstName: "Amaka",
            lastName: "Okafor",
            otherNames: "Chinwe",
            email: "amaka@mova.app",
            phone: "+2348012345678",
            profilePicture: "https://cdn.mova.app/amaka.png");

        var result = await CreateHandler().Handle(CreateQuery(), default);

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
        await SeedProfileUserAsync(profilePicture: null);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.Null(result.Data!.ProfilePicture);
    }

    [Fact]
    public async Task Handle_ReturnsBalanceAsDecimal()
    {
        await SeedProfileUserAsync(balanceNaira: 25_750.50m);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.Equal(25_750.50m, result.Data!.Balance);
    }

    [Fact]
    public async Task Handle_ReturnsCreatedAt()
    {
        var createdAt = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        await SeedProfileUserAsync(createdAt: createdAt);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.Equal(createdAt, result.Data!.CreatedAt);
    }

    [Fact]
    public async Task Handle_MapsNotificationPreferences_AllEnabled()
    {
        await SeedProfileUserAsync(
            notifyLoginAlerts: true,
            notifyReleaseAlerts: true,
            notifyProductUpdates: true,
            notifyPromotions: true);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var n = result.Data!.Notifications;
        Assert.True(n.Login);
        Assert.True(n.Release);
        Assert.True(n.Updates);
        Assert.True(n.Promotions);
    }

    [Fact]
    public async Task Handle_MapsNotificationPreferences_AllDisabled()
    {
        await SeedProfileUserAsync(
            notifyLoginAlerts: false,
            notifyReleaseAlerts: false,
            notifyProductUpdates: false,
            notifyPromotions: false);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var n = result.Data!.Notifications;
        Assert.False(n.Login);
        Assert.False(n.Release);
        Assert.False(n.Updates);
        Assert.False(n.Promotions);
    }

    [Fact]
    public async Task Handle_MapsNotificationPreferences_MixedValues()
    {
        await SeedProfileUserAsync(
            notifyLoginAlerts: true,
            notifyReleaseAlerts: false,
            notifyProductUpdates: true,
            notifyPromotions: false);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var n = result.Data!.Notifications;
        Assert.True(n.Login);
        Assert.False(n.Release);
        Assert.True(n.Updates);
        Assert.False(n.Promotions);
    }

    [Fact]
    public async Task Handle_WithPinHashSet_HasPinSetIsTrue()
    {
        await SeedProfileUserAsync(transactionPinHash: "hashed-pin-value");

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.True(result.Data!.HasPinSet);
    }

    [Fact]
    public async Task Handle_WithEmptyPinHash_HasPinSetIsFalse()
    {
        await SeedProfileUserAsync(transactionPinHash: "");

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.False(result.Data!.HasPinSet);
    }

    [Fact]
    public async Task Handle_WithWhitespacePinHash_HasPinSetIsFalse()
    {
        await SeedProfileUserAsync(transactionPinHash: "   ");

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.False(result.Data!.HasPinSet);
    }

    [Fact]
    public async Task Handle_WithNullPinHash_HasPinSetIsFalse()
    {
        await SeedProfileUserAsync(transactionPinHash: null);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.False(result.Data!.HasPinSet);
    }

    [Fact]
    public async Task Handle_AlwaysIncludesAccountStatus()
    {
        await SeedProfileUserAsync();

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.NotNull(result.Data!.AccountStatus);
    }

    [Fact]
    public async Task Handle_WithActiveAccount_StatusIsHealthy()
    {
        await SeedProfileUserAsync(
            accountStatus: UserAccountStatus.Active,
            restrictionReason: AccountRestrictionReason.None,
            restrictionExpiresAt: null);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Active", status.Status);
        Assert.True(status.IsHealthy);
        Assert.Null(status.RestrictionReason);
        Assert.Null(status.RestrictionExpiresAt);
    }

    [Fact]
    public async Task Handle_WithRestrictedAccount_IsNotHealthy()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        await SeedProfileUserAsync(
            accountStatus: UserAccountStatus.Restricted,
            restrictionReason: AccountRestrictionReason.TermsViolation,
            restrictionReasonDetails: "Three warnings in 30 days.",
            restrictionExpiresAt: expiresAt);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Restricted", status.Status);
        Assert.False(status.IsHealthy);
        Assert.Equal("TermsViolation", status.RestrictionReason);
        Assert.Equal("Three warnings in 30 days.", status.RestrictionReasonDetails);
        Assert.Equal(expiresAt, status.RestrictionExpiresAt);
    }

    [Fact]
    public async Task Handle_WithSuspendedAccount_IsNotHealthy()
    {
        await SeedProfileUserAsync(
            accountStatus: UserAccountStatus.Suspended,
            restrictionReason: AccountRestrictionReason.SuspectedFraud,
            restrictionReasonDetails: "Chargebacks from 4 cards.");

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Suspended", status.Status);
        Assert.False(status.IsHealthy);
        Assert.Equal("SuspectedFraud", status.RestrictionReason);
    }

    [Fact]
    public async Task Handle_WithClosedAccount_IsNotHealthy()
    {
        await SeedProfileUserAsync(
            accountStatus: UserAccountStatus.Closed,
            restrictionReason: AccountRestrictionReason.UserRequestedDeactivation);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Closed", status.Status);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WithDeactivatedAccount_IsNotHealthy()
    {
        await SeedProfileUserAsync(accountStatus: UserAccountStatus.Deactivated);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Deactivated", status.Status);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WithActiveStatusAndExpiredRestriction_IsNotHealthy()
    {
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        await SeedProfileUserAsync(
            accountStatus: UserAccountStatus.Active,
            restrictionExpiresAt: expiredAt);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var status = result.Data!.AccountStatus;
        Assert.Equal("Active", status.Status);
        Assert.False(status.IsHealthy);
    }

    [Fact]
    public async Task Handle_WithActiveStatusAndFutureRestriction_IsNotHealthy()
    {
        var futureAt = DateTimeOffset.UtcNow.AddMinutes(5);

        await SeedProfileUserAsync(
            accountStatus: UserAccountStatus.Active,
            restrictionExpiresAt: futureAt);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.False(result.Data!.AccountStatus.IsHealthy);
    }

    [Fact]
    public async Task Handle_NoPendingChange_ReturnsNull()
    {
        await SeedProfileUserAsync();

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.Null(result.Data!.PendingProfileChange);
    }

    [Fact]
    public async Task Handle_WithPendingChange_ReturnsIt()
    {
        await SeedProfileUserAsync();
        var submittedAt = DateTimeOffset.UtcNow.AddHours(-1);

        await SeedPendingChangeAsync(
            requestReason: "Name change",
            requestComment: "Marriage",
            createdAt: submittedAt);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var pending = result.Data!.PendingProfileChange;
        Assert.NotNull(pending);
        Assert.Equal("Name change", pending!.RequestReason);
        Assert.Equal("Marriage", pending.RequestComment);
        Assert.Equal(submittedAt, pending.SubmittedAt);
    }

    [Fact]
    public async Task Handle_WithApprovedChange_ReturnsNull()
    {
        await SeedProfileUserAsync();
        await SeedPendingChangeAsync(isApproved: true);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.Null(result.Data!.PendingProfileChange);
    }

    [Fact]
    public async Task Handle_WithRejectedChange_ReturnsIt()
    {
        await SeedProfileUserAsync();
        var rejectedAt = DateTimeOffset.UtcNow.AddHours(-1);

        await SeedPendingChangeAsync(
            isRejected: true,
            rejectionReason: "Does not match legal ID",
            rejectedAt: rejectedAt);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        var pending = result.Data!.PendingProfileChange;
        Assert.NotNull(pending);
        Assert.Equal("Does not match legal ID", pending!.RejectionReason);
        Assert.Equal(rejectedAt, pending.RejectedAt);
    }

    [Fact]
    public async Task Handle_WhenMultiplePendingChanges_ReturnsMostRecent()
    {
        await SeedProfileUserAsync();
        var older = DateTimeOffset.UtcNow.AddDays(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);

        await SeedPendingChangeAsync(requestReason: "Older", createdAt: older);
        await SeedPendingChangeAsync(requestReason: "Newer", createdAt: newer);

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.Equal("Newer", result.Data!.PendingProfileChange!.RequestReason);
    }

    [Fact]
    public async Task Handle_PendingChangeBelongingToAnotherUser_IsIgnored()
    {
        await SeedProfileUserAsync();
        await SeedPendingChangeAsync(
            userPublicId: "someone-else",
            requestReason: "Should not appear");

        var result = await CreateHandler().Handle(CreateQuery(), default);

        Assert.Null(result.Data!.PendingProfileChange);
    }
}