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

public sealed class SetPinCommandTests : BaseTest
{
    private const string UserPublicId = "user_setpin_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";
    private const string Pin = "123456";

    private readonly Mock<ITransactionPinService> _pinService = new();
    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private SetPinCommand.Handler CreateHandler()
    {
        return new SetPinCommand.Handler(
            _pinService.Object,
            _identityService.Object,
            _notifications.Object,
            Mock.Of<ILogger<SetPinCommand.Handler>>());
    }

    private SetPinCommand.Command CreateCommand(
        string pin = Pin,
        string? userPublicId = null)
    {
        return new SetPinCommand.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            Pin = pin,
        };
    }

    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------

    private void SetupUserExists(
        long userId = UserId,
        string publicId = UserPublicId)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                userId,
                publicId,
                UserFirstName,
                null,
                "Starboy",
                UserEmail,
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

    private void SetupHasPin(bool hasPin = false)
    {
        _pinService
            .Setup(x => x.HasPinAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasPin);
    }

    private void SetupSetPinSucceeds()
    {
        _pinService
            .Setup(x => x.SetPinAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupSetPinThrows(Exception exception)
    {
        _pinService
            .Setup(x => x.SetPinAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    // ---------------------------------------------------------
    // 1. Validation
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
    public async Task Handle_WithEmptyPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(pin: ""), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("PIN is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithShortPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(pin: "12345"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("exactly 6", result.Message);
    }

    [Fact]
    public async Task Handle_WithNonDigitPin_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(pin: "12345a"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("only digits", result.Message);
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
    // 3. Already has PIN
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPinAlreadyExists_ReturnsConflict()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("Transaction PIN has already been set.", result.Message);

        _pinService.Verify(
            x => x.SetPinAsync(
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
        SetupHasPin(hasPin: false);
        SetupSetPinSucceeds();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Transaction PIN created successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CallsSetPinOnce()
    {
        SetupUserExists();
        SetupHasPin(hasPin: false);
        SetupSetPinSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _pinService.Verify(
            x => x.SetPinAsync(
                UserPublicId,
                Pin,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ChecksHasPin()
    {
        SetupUserExists();
        SetupHasPin(hasPin: false);
        SetupSetPinSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _pinService.Verify(
            x => x.HasPinAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 5. SetPin exceptions
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenSetPinThrowsArgumentException_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin(hasPin: false);
        SetupSetPinThrows(new ArgumentException("Invalid"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("The PIN format is invalid.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenSetPinThrowsInvalidOperationException_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin(hasPin: false);
        SetupSetPinThrows(new InvalidOperationException("Not allowed"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("The PIN operation could not be completed.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenSetPinThrowsGenericException_ReturnsInternalServerError()
    {
        SetupUserExists();
        SetupHasPin(hasPin: false);
        SetupSetPinThrows(new Exception("Unexpected"));

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
        SetupHasPin(hasPin: false);
        SetupSetPinSucceeds();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Security,
                "Transaction PIN created",
                It.Is<string>(m => m.Contains("transaction PIN was created")),
                "/settings",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSetPinFails_DoesNotSendNotification()
    {
        SetupUserExists();
        SetupHasPin(hasPin: false);
        SetupSetPinThrows(new Exception("Boom"));

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
        SetupHasPin(hasPin: false);
        SetupSetPinSucceeds();

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
}