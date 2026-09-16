using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.TransactionPin;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class ChangePinCommandTests : BaseTest
{
    private const string UserPublicId = "user_changepin_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";
    private const string CurrentPin = "123456";
    private const string NewPin = "654321";

    private readonly Mock<ITransactionPinService> _pinService = new();
    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private ChangePinCommand.Handler CreateHandler()
    {
        return new ChangePinCommand.Handler(
            _pinService.Object,
            _identityService.Object,
            _notifications.Object,
            Mock.Of<ILogger<ChangePinCommand.Handler>>());
    }

    private ChangePinCommand.Command CreateCommand(
        string currentPin = CurrentPin,
        string newPin = NewPin,
        string? userPublicId = null)
    {
        return new ChangePinCommand.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            CurrentPin = currentPin,
            NewPin = newPin,
        };
    }

    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------

    private void SetupUserExists(
        long userId = UserId,
        string publicId = UserPublicId,
        string email = UserEmail,
        string firstName = UserFirstName)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                userId,
                publicId,
                firstName,
                null,
                "Starboy",
                email,
                "08050000000",
                null,
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

    private void SetupHasPin(bool hasPin = true)
    {
        _pinService
            .Setup(x => x.HasPinAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasPin);
    }

    private void SetupVerifyPin(bool isValid = true)
    {
        _pinService
            .Setup(x => x.VerifyPinAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(isValid);
    }

    private void SetupChangePinSucceeds()
    {
        _pinService
            .Setup(x => x.ChangePinAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupChangePinThrows(Exception exception)
    {
        _pinService
            .Setup(x => x.ChangePinAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    // ---------------------------------------------------------
    // 1. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(userPublicId: ""),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("UserPublicId is required.", result.Message);
        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithEmptyCurrentPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(currentPin: ""),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Current PIN is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithShortCurrentPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(currentPin: "12345"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("exactly 6", result.Message);
    }

    [Fact]
    public async Task Handle_WithNonDigitCurrentPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(currentPin: "12345a"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("only digits", result.Message);
    }

    [Fact]
    public async Task Handle_WithEmptyNewPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(newPin: ""),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("New PIN is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithShortNewPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(newPin: "65432"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("exactly 6", result.Message);
    }

    [Fact]
    public async Task Handle_WithNonDigitNewPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(newPin: "65432b"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("only digits", result.Message);
    }

    [Fact]
    public async Task Handle_WhenNewPinEqualsCurrentPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(currentPin: "123456", newPin: "123456"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("cannot be the same", result.Message);
    }

    // ---------------------------------------------------------
    // 2. User lookup
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsBadRequest()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
    }

    // ---------------------------------------------------------
    // 3. PIN state
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenNoPinSet_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin(hasPin: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Transaction PIN has not been set.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenCurrentPinIsWrong_ReturnsUnauthorized()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPin(isValid: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal("Invalid current PIN.", result.Message);

        _pinService.Verify(
            x => x.ChangePinAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 4. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsSuccess()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinSucceeds();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Transaction PIN changed successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsChangePinOnce()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _pinService.Verify(
            x => x.ChangePinAsync(
                UserPublicId,
                NewPin,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsVerifyPinWithCurrentPin()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _pinService.Verify(
            x => x.VerifyPinAsync(
                UserPublicId,
                CurrentPin,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_DoesNotCallChangePinWhenVerifyFails()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin(isValid: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _pinService.Verify(
            x => x.ChangePinAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 5. ChangePin exceptions
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenChangePinThrowsArgumentException_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinThrows(new ArgumentException("Invalid PIN format"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("The PIN format is invalid.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenChangePinThrowsInvalidOperationException_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinThrows(new InvalidOperationException("Operation not allowed"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("The PIN operation could not be completed.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenChangePinThrowsGenericException_ReturnsInternalServerError()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinThrows(new Exception("Unexpected"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Contains("An error occurred", result.Message);
    }

    // ---------------------------------------------------------
    // 6. Notifications
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_QueuesInAppNotification()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Security,
                "Transaction PIN changed",
                It.Is<string>(m => m.Contains("transaction PIN was changed")),
                "/settings",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_QueuesEmailNotification()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m => m.Contains("transaction PIN was changed")),
                "Your MOVA transaction PIN was changed"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenChangePinFails_DoesNotSendNotification()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinThrows(new Exception("Boom"));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

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

    [Fact]
    public async Task Handle_WhenCurrentPinIsWrong_DoesNotSendNotification()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin(isValid: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

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

    // ---------------------------------------------------------
    // 7. Regression: notification failures must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillReturnsSuccess()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinSucceeds();

        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Redis down"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenEmailQueueThrows_StillReturnsSuccess()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinSucceeds();

        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Hangfire unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenBothNotificationsThrow_StillReturnsSuccess()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinSucceeds();

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
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();
        SetupChangePinSucceeds();

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
}