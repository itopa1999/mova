using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.TransactionPin;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Security;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class VerifyPinCommandTests : BaseTest
{
    private const string UserPublicId = "user_verifypin_test";
    private const long UserId = 42;
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";
    private const string Pin = "123456";

    private readonly Mock<ITransactionPinService> _pinService = new();
    private readonly Mock<IIdentityService> _identityService = new();

    private VerifyPinCommand.Handler CreateHandler()
    {
        return new VerifyPinCommand.Handler(
            _pinService.Object,
            _identityService.Object,
            Mock.Of<ILogger<VerifyPinCommand.Handler>>());
    }

    private VerifyPinCommand.Command CreateCommand(
        string pin = Pin,
        string? userPublicId = null)
    {
        return new VerifyPinCommand.Command
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

    private void SetupVerifyPinThrows(Exception exception)
    {
        _pinService
            .Setup(x => x.VerifyPinAsync(
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

    [Fact]
    public async Task Handle_WithUnknownUser_DoesNotCheckPin()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _pinService.Verify(
            x => x.HasPinAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _pinService.Verify(
            x => x.VerifyPinAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 3. No PIN set
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

        _pinService.Verify(
            x => x.VerifyPinAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------
    // 4. Invalid PIN
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithInvalidPin_ReturnsUnauthorized()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPin(isValid: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal("Invalid transaction PIN.", result.Message);
    }

    // ---------------------------------------------------------
    // 5. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidPin_ReturnsSuccess()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPin(isValid: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Transaction PIN verified successfully.", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidPin_CallsVerifyPinOnce()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPin(isValid: true);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _pinService.Verify(
            x => x.VerifyPinAsync(
                UserPublicId,
                Pin,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidPin_ChecksHasPinFirst()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPin(isValid: true);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _pinService.Verify(
            x => x.HasPinAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 6. Exceptions from the PIN service
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenVerifyPinThrowsArgumentException_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPinThrows(new ArgumentException("Invalid format"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("The PIN format is invalid.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenVerifyPinThrowsInvalidOperationException_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPinThrows(new InvalidOperationException("Not allowed"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("The PIN operation could not be completed.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenVerifyPinThrowsGenericException_ReturnsInternalServerError()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPinThrows(new Exception("Unexpected"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Contains("An error occurred", result.Message);
    }

    [Fact]
    public async Task Handle_WhenHasPinThrowsException_ReturnsInternalServerError()
    {
        SetupUserExists();
        _pinService
            .Setup(x => x.HasPinAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB down"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
    }

    // ---------------------------------------------------------
    // 7. Cancellation propagation
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidPin_PassesCancellationToken()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPin(isValid: true);

        using var cts = new CancellationTokenSource();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), cts.Token);

        _pinService.Verify(
            x => x.VerifyPinAsync(
                UserPublicId,
                Pin,
                cts.Token),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 8. BaseResult has no Data payload
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidPin_ReturnsResultWithoutPayload()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPin(isValid: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        // Non-generic BaseResult — no Data property to assert on.
        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
}