using System.Net;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.Authentication;
using Mova.Application.BBL.Queries.Profile;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class RequestAccountDeletionTests : BaseTestWithPinChallenge
{
    private const string UserPublicId = "user_delete_test";
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";

    private readonly Mock<INotificationQueue> _notifications = new();
    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IRefreshTokenService> _refreshTokenService = new();
    private readonly Mock<IMediator> _mediator = new();

    public RequestAccountDeletionTests()
    {
        SetupPinChallengeValid();
        SetupUserFound();
        SetupEligible();
        SetupCloseSuccess();
        SetupRevokeSuccess();
    }

    private RequestAccountDeletion.Handler CreateHandler()
    {
        return new RequestAccountDeletion.Handler(
            _mediator.Object,
            _identityService.Object,
            PinChallenge.Object,
            _refreshTokenService.Object,
            _notifications.Object,
            Mock.Of<ILogger<RequestAccountDeletion.Handler>>());
    }

    private static RequestAccountDeletion.Command CreateCommand(
        string? userPublicId = null,
        string? reason = null)
    {
        return new RequestAccountDeletion.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            Reason = reason,
        };
    }

    // ─── Setup helpers ─────────────────────────────────────

    private void SetupPinChallengeValid()
    {
        PinChallenge
            .Setup(x => x.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));
    }

    private void SetupPinChallengeInvalid(string message = "PIN challenge expired.")
    {
        PinChallenge
            .Setup(x => x.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, message));
    }

    private void SetupUserFound(
        decimal mainBalanceNaira = 0m,
        string firstName = UserFirstName,
        string email = UserEmail)
    {
        var dto = BuildUserIdentityDto(
            firstName: firstName,
            email: email,
            mainBalanceNaira: mainBalanceNaira);

        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
    }

    private void SetupUserNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);
    }

    private static UserIdentityDto BuildUserIdentityDto(
        string firstName,
        string email,
        decimal mainBalanceNaira,
        long id = 1)
    {
        return new UserIdentityDto(
            id,
            UserPublicId,
            firstName,
            null,
            "User",
            email,
            "08000000000",
            null,
            Money.FromNaira(mainBalanceNaira),
            string.Empty,
            true,
            true,
            true,
            false,
            string.Empty,
            DateTimeOffset.UtcNow);
    }

    private void SetupEligible(bool canDelete = true)
    {
        var dto = new GetRequestForAccountDeletion.GetRequestForAccountDeletionDto
        {
            CanRequestAccountDeletion = canDelete,
        };

        _mediator
            .Setup(x => x.Send(
                It.IsAny<GetRequestForAccountDeletion.Query>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new BaseResult<GetRequestForAccountDeletion.GetRequestForAccountDeletionDto>(
                    HttpStatusCode.OK,
                    "Account deletion eligibility retrieved successfully.",
                    dto));
    }

    private void SetupEligibilityQueryFailed(
        HttpStatusCode status = HttpStatusCode.BadRequest,
        string message = "Eligibility failed.")
    {
        _mediator
            .Setup(x => x.Send(
                It.IsAny<GetRequestForAccountDeletion.Query>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new BaseResult<GetRequestForAccountDeletion.GetRequestForAccountDeletionDto>(
                    status,
                    message));
    }

    private void SetupCloseSuccess(bool result = true)
    {
        _identityService
            .Setup(x => x.CloseAccountAsync(
                It.IsAny<string>(),
                It.IsAny<AccountRestrictionReason>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private void SetupCloseThrows(Exception exception)
    {
        _identityService
            .Setup(x => x.CloseAccountAsync(
                It.IsAny<string>(),
                It.IsAny<AccountRestrictionReason>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    private void SetupRevokeSuccess(int revokedCount = 3)
    {
        _refreshTokenService
            .Setup(x => x.RevokeAllForUserAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(revokedCount);
    }

    private void SetupRevokeThrows(Exception exception)
    {
        _refreshTokenService
            .Setup(x => x.RevokeAllForUserAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    // ─────────────────────────────────────────────────────
    // 1. Happy path
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsSuccess()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(
            "Your account deletion request has been submitted.",
            result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ClosesTheAccount()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.CloseAccountAsync(
                UserPublicId,
                AccountRestrictionReason.UserRequestedDeactivation,
                It.Is<string>(d => d.Contains("User requested account deletion")),
                "self-service",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_RevokesAllRefreshTokens()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _refreshTokenService.Verify(
            x => x.RevokeAllForUserAsync(
                UserPublicId,
                "Account closed",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SendsInAppNotification()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Account,
                "Account deletion confirmed",
                It.Is<string>(m => m.Contains("account has been closed")),
                "/",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_QueuesEmailNotification()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m => m.Contains("successfully closed")),
                "Your MOVA account has been closed"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithCustomReason_IncludesReasonInDetails()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(reason: "privacy"), default);

        _identityService.Verify(
            x => x.CloseAccountAsync(
                UserPublicId,
                It.IsAny<AccountRestrictionReason>(),
                It.Is<string>(d => d.Contains("privacy")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithNoReason_UsesDefaultDetails()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(reason: null), default);

        _identityService.Verify(
            x => x.CloseAccountAsync(
                UserPublicId,
                It.IsAny<AccountRestrictionReason>(),
                "User requested account deletion.",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────
    // 2. User identity validation
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(userPublicId: ""),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User identity is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_DoesNotValidatePin()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(userPublicId: ""), default);

        PinChallenge.Verify(
            x => x.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_DoesNotCloseAccount()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(userPublicId: ""), default);

        _identityService.Verify(
            x => x.CloseAccountAsync(
                It.IsAny<string>(),
                It.IsAny<AccountRestrictionReason>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────
    // 3. PIN challenge
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenPinChallengeFails_ReturnsBadRequest()
    {
        SetupPinChallengeInvalid("PIN challenge expired.");
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("PIN challenge expired.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenPinChallengeFails_DoesNotLoadUser()
    {
        SetupPinChallengeInvalid();
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPinChallengeFails_DoesNotCloseAccount()
    {
        SetupPinChallengeInvalid();
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.CloseAccountAsync(
                It.IsAny<string>(),
                It.IsAny<AccountRestrictionReason>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────
    // 4. User lookup
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFound()
    {
        SetupUserNotFound();
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_DoesNotQueryEligibility()
    {
        SetupUserNotFound();
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _mediator.Verify(
            x => x.Send(
                It.IsAny<GetRequestForAccountDeletion.Query>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────
    // 5. Main balance threshold
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenMainBalanceAboveThreshold_ReturnsBadRequest()
    {
        SetupUserFound(mainBalanceNaira: 101m);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("₦100", result.Message);
    }

    [Fact]
    public async Task Handle_WhenMainBalanceExactlyThreshold_Passes()
    {
        SetupUserFound(mainBalanceNaira: 100m);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WhenMainBalanceAboveThreshold_DoesNotQueryEligibility()
    {
        SetupUserFound(mainBalanceNaira: 500m);
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _mediator.Verify(
            x => x.Send(
                It.IsAny<GetRequestForAccountDeletion.Query>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMainBalanceAboveThreshold_DoesNotCloseAccount()
    {
        SetupUserFound(mainBalanceNaira: 500m);
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.CloseAccountAsync(
                It.IsAny<string>(),
                It.IsAny<AccountRestrictionReason>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────
    // 6. Eligibility
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenEligibilityQueryFails_ReturnsThatStatusCode()
    {
        SetupEligibilityQueryFailed(HttpStatusCode.NotFound, "Account not found.");
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Account not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenNotEligible_ReturnsBadRequest()
    {
        SetupEligible(canDelete: false);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("not eligible", result.Message);
    }

    [Fact]
    public async Task Handle_WhenNotEligible_DoesNotCloseAccount()
    {
        SetupEligible(canDelete: false);
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _identityService.Verify(
            x => x.CloseAccountAsync(
                It.IsAny<string>(),
                It.IsAny<AccountRestrictionReason>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────
    // 7. Close account
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenCloseReturnsFalse_ReturnsInternalServerError()
    {
        SetupCloseSuccess(result: false);
        var handler = CreateHandler();

        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal(
            "Failed to submit your account deletion request. Please try again.",
            result.Message);
    }

    [Fact]
    public async Task Handle_WhenCloseReturnsFalse_DoesNotRevokeTokens()
    {
        SetupCloseSuccess(result: false);
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _refreshTokenService.Verify(
            x => x.RevokeAllForUserAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCloseThrows_PropagatesException()
    {
        SetupCloseThrows(new InvalidOperationException("DB down"));
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(CreateCommand(), default));
    }

    [Fact]
    public async Task Handle_WhenCloseThrows_DoesNotRevokeTokens()
    {
        SetupCloseThrows(new InvalidOperationException("DB down"));
        var handler = CreateHandler();

        try { await handler.Handle(CreateCommand(), default); }
        catch (InvalidOperationException) { /* swallow — assertion is on the mock */ }

        _refreshTokenService.Verify(
            x => x.RevokeAllForUserAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────
    // 8. Refresh token revocation
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenRevokeThrows_PropagatesException()
    {
        SetupRevokeThrows(new InvalidOperationException("Cache down"));
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(CreateCommand(), default));
    }

    [Fact]
    public async Task Handle_WhenRevokeThrows_DoesNotSendNotification()
    {
        SetupRevokeThrows(new InvalidOperationException("Cache down"));
        var handler = CreateHandler();

        try { await handler.Handle(CreateCommand(), default); }
        catch (InvalidOperationException) { /* swallow */ }

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────
    // 9. Notification resilience
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillReturnsSuccess()
    {
        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Exception("Redis down"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenEmailQueueThrows_StillReturnsSuccess()
    {
        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new Exception("Hangfire down"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenBothNotificationsThrow_StillReturnsSuccess()
    {
        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Exception("boom"));

        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new Exception("boom"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WhenInAppThrows_EmailIsStillAttempted()
    {
        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Exception("Redis down"));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────
    // 10. Ordering guarantees
    // ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_OrderIsCloseThenRevokeThenNotify()
    {
        var calls = new List<string>();

        _identityService
            .Setup(x => x.CloseAccountAsync(
                It.IsAny<string>(),
                It.IsAny<AccountRestrictionReason>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("close"))
            .ReturnsAsync(true);

        _refreshTokenService
            .Setup(x => x.RevokeAllForUserAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("revoke"))
            .ReturnsAsync(1);

        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("inapp"));

        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback(() => calls.Add("email"));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(new[] { "close", "revoke", "inapp", "email" }, calls);
    }
}