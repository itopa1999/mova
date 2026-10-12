using Moq;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Enums;

namespace Mova.Tests;

public abstract class BaseTestWithPinChallenge : BaseTest
{
    protected Mock<IPinChallenge> PinChallenge { get; } = new();

    protected BaseTestWithPinChallenge()
    {
        SetupPinChallengeSucceeds();
    }

    protected void SetupPinChallengeSucceeds()
        => SetupPinChallenge(isValid: true, message: string.Empty);

    protected void SetupPinChallengeFails(string message = "Please verify your PIN again.")
        => SetupPinChallenge(isValid: false, message: message);

    protected void SetupPinChallenge(bool isValid, string message)
    {
        PinChallenge
            .Setup(x => x.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((isValid, message));
    }

    protected void VerifyPinChallengeValidated(
        string userPublicId,
        PinChallengeType challengeType,
        Times times)
    {
        PinChallenge.Verify(
            x => x.ValidateAsync(
                userPublicId,
                challengeType,
                It.IsAny<CancellationToken>()),
            times);
    }

    protected void VerifyPinChallengeNeverValidated()
        => PinChallenge.Verify(
            x => x.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
}