using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.TransactionPin;
using Mova.Application.Behaviors;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

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
    private readonly Mock<INotificationQueue> _notificationQueue = new();

    private VerifyPinCommand.Handler CreateHandler()
    {
        return new VerifyPinCommand.Handler(
            _pinService.Object,
            _identityService.Object,
            _notificationQueue.Object,
            Mock.Of<ILogger<VerifyPinCommand.Handler>>());
    }

    private VerifyPinCommand.Command CreateCommand(
        string pin = Pin,
        string? userPublicId = null,
        PinChallengeType pinChallengeType = PinChallengeType.CreateWallet)
    {
        return new VerifyPinCommand.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            Pin = pin,
            PinChallengeType = pinChallengeType,
        };
    }

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
                string.Empty,
                false,
                false,
                false,
                false,
                string.Empty,
                DateTimeOffset.UtcNow));
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
            .Setup(x => x.VerifyPinWithStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(isValid
                ? TransactionPinVerificationResult.Verified
                : TransactionPinVerificationResult.Invalid);
    }

    private void SetupVerifyPinThrows(Exception exception)
    {
        _pinService
            .Setup(x => x.VerifyPinWithStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

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
            x => x.VerifyPinWithStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

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
            x => x.VerifyPinWithStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

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

    [Fact]
    public async Task Handle_WhenPinIsNewlyLocked_ReturnsLockedAndQueuesEmail()
    {
        SetupUserExists();
        SetupHasPin();
        _pinService
            .Setup(x => x.VerifyPinWithStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionPinVerificationResult.LockedNow);

        var result = await CreateHandler().Handle(CreateCommand(), default);

        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
        Assert.Contains("locked for 1 hour", result.Message);
        _notificationQueue.Verify(x => x.QueueNotificationEmail(
            UserFirstName,
            UserEmail,
            It.Is<string>(message => message.Contains("locked your transaction PIN for 1 hour")),
            "Transaction PIN temporarily locked"), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPinIsAlreadyLocked_ReturnsLockedWithoutSendingAnotherEmail()
    {
        SetupUserExists();
        SetupHasPin();
        _pinService
            .Setup(x => x.VerifyPinWithStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionPinVerificationResult.Locked);

        var result = await CreateHandler().Handle(CreateCommand(), default);

        Assert.Equal(HttpStatusCode.Locked, result.StatusCode);
        Assert.Contains("try again in 1 hour", result.Message);
        _notificationQueue.Verify(x => x.QueueNotificationEmail(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

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
            x => x.VerifyPinWithStatusAsync(
                UserPublicId,
                Pin,
                It.IsAny<PinChallengeType>(),
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
            x => x.VerifyPinWithStatusAsync(
                UserPublicId,
                Pin,
                It.IsAny<PinChallengeType>(),
                cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidPin_ReturnsResultWithoutPayload()
    {
        SetupUserExists();
        SetupHasPin(hasPin: true);
        SetupVerifyPin(isValid: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithUndefinedChallengeType_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();

        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(pinChallengeType: (PinChallengeType)0),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("A valid PIN challenge type is required.", result.Message);

        _pinService.Verify(
            x => x.VerifyPinWithStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithUnknownNumericChallengeType_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();

        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(pinChallengeType: (PinChallengeType)999),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("A valid PIN challenge type is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithDisallowedChallengeType_ReturnsBadRequest()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();

        var handler = CreateHandler();

        var result = await handler.Handle(
            CreateCommand(pinChallengeType: PinChallengeType.ChangeTransactionPin),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(
            "Unsupported PIN challenge type for this endpoint.",
            result.Message);

        _pinService.Verify(
            x => x.VerifyPinWithStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(PinChallengeType.CreateWallet)]
    [InlineData(PinChallengeType.AddBankAccount)]
    [InlineData(PinChallengeType.BankAccountDeletion)]
    [InlineData(PinChallengeType.ManualTransferToBank)]
    [InlineData(PinChallengeType.ManualPayUtilities)]
    [InlineData(PinChallengeType.BreakWallet)]
    [InlineData(PinChallengeType.PausedWallet)]
    [InlineData(PinChallengeType.UnpausedWallet)]
    [InlineData(PinChallengeType.RestartWallet)]
    [InlineData(PinChallengeType.LinkBankAccountToWallet)]
    [InlineData(PinChallengeType.WalletAutomation)]
    public async Task Handle_WithEachAllowedChallengeType_ReturnsSuccess(
        PinChallengeType challengeType)
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();

        var result = await CreateHandler().Handle(
            CreateCommand(pinChallengeType: challengeType),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithValidPin_ForwardsChallengeTypeToService()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();

        await CreateHandler().Handle(
            CreateCommand(pinChallengeType: PinChallengeType.BreakWallet),
            default);

        _pinService.Verify(
            x => x.VerifyPinWithStatusAsync(
                UserPublicId,
                Pin,
                PinChallengeType.BreakWallet,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(PinChallengeType.CreateWallet)]
    [InlineData(PinChallengeType.AddBankAccount)]
    [InlineData(PinChallengeType.BreakWallet)]
    [InlineData(PinChallengeType.RestartWallet)]
    public async Task Handle_WithValidPin_DoesNotSubstituteChallengeType(
        PinChallengeType challengeType)
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPin();

        await CreateHandler().Handle(
            CreateCommand(pinChallengeType: challengeType),
            default);

        _pinService.Verify(
            x => x.VerifyPinWithStatusAsync(
                UserPublicId,
                Pin,
                challengeType,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _pinService.Verify(
            x => x.VerifyPinWithStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.Is<PinChallengeType>(t => t != challengeType),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidChallengeType_DoesNotCheckHasPin()
    {
        SetupUserExists();

        await CreateHandler().Handle(
            CreateCommand(pinChallengeType: (PinChallengeType)0),
            default);

        _pinService.Verify(
            x => x.HasPinAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithInvalidChallengeType_DoesNotLookupUser()
    {
        var handler = CreateHandler();

        await handler.Handle(
            CreateCommand(pinChallengeType: (PinChallengeType)0),
            default);

        _identityService.Verify(
            x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenChallengePersistenceFails_ReturnsInternalServerError()
    {
        SetupUserExists();
        SetupHasPin();
        SetupVerifyPinThrows(new PinChallengePersistenceException(
            "Failed to persist PIN challenge after successful verification."));

        var result = await CreateHandler().Handle(
            CreateCommand(pinChallengeType: PinChallengeType.CreateWallet),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Contains("An error occurred", result.Message);
    }
}