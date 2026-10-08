using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.MovaAPIs;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class SubmitFeedbackCommandTests : BaseTest
{
    private const string UserPublicId = "user_feedback_test";
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";

    private const string AcknowledgmentMessage =
        "Thank you for your feedback. We read every message.";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private SubmitFeedbackCommand.Handler CreateHandler()
    {
        return new SubmitFeedbackCommand.Handler(
            UnitOfWork,
            _identityService.Object,
            _notifications.Object,
            Mock.Of<ILogger<SubmitFeedbackCommand.Handler>>());
    }

    private SubmitFeedbackCommand.Command CreateCommand(
        int rating = 5,
        string experience = "great",
        List<string>? improvements = null,
        string message = "Love the app!")
    {
        return new SubmitFeedbackCommand.Command
        {
            UserPublicId = UserPublicId,
            Email = UserEmail,
            FirstName = UserFirstName,
            Rating = rating,
            Experience = experience,
            Improvements = improvements ?? new List<string> { "features", "speed" },
            Message = message,
        };
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithFullFeedback_PersistsFeedbackToDatabase()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);

        var feedback = await Context.Feedbacks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserPublicId == UserPublicId);

        Assert.NotNull(feedback);
        Assert.Equal(5, feedback!.Rating);
        Assert.Equal("great", feedback.Experience);
        Assert.Equal("features,speed", feedback.Improvements);
        Assert.Equal("Love the app!", feedback.Message);
    }

    [Fact]
    public async Task Handle_WithFullFeedback_ReturnsCreatedStatus()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithFullFeedback_ReturnsAcknowledgmentMessage()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.Equal(AcknowledgmentMessage, result.Data!.AcknowledgmentMessage);
    }

    [Fact]
    public async Task Handle_WithFullFeedback_ReturnsNotificationTrueWhenDelivered()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.NotNull(result.Data);
        Assert.True(result.Data!.Notification);
    }

    [Fact]
    public async Task Handle_WithFullFeedback_MarksFeedbackAsNotDone()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var feedback = await Context.Feedbacks
            .AsNoTracking()
            .FirstAsync(x => x.UserPublicId == UserPublicId);

        Assert.False(feedback.IsDone);
    }

    // ---------------------------------------------------------
    // 2. Partial feedback — each field alone is enough
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithOnlyRating_Succeeds()
    {
        var command = CreateCommand();
        command.Experience = string.Empty;
        command.Improvements = new List<string>();
        command.Message = string.Empty;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithOnlyExperience_Succeeds()
    {
        var command = CreateCommand();
        command.Rating = 0;
        command.Improvements = new List<string>();
        command.Message = string.Empty;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WithOnlyImprovements_Succeeds()
    {
        var command = CreateCommand();
        command.Rating = 0;
        command.Experience = string.Empty;
        command.Message = string.Empty;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WithOnlyMessage_Succeeds()
    {
        var command = CreateCommand();
        command.Rating = 0;
        command.Experience = string.Empty;
        command.Improvements = new List<string>();

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
    }

    // ---------------------------------------------------------
    // 3. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var command = CreateCommand();
        command.UserPublicId = string.Empty;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithNoFeedbackFields_ReturnsBadRequest()
    {
        var command = CreateCommand();
        command.Rating = 0;
        command.Experience = string.Empty;
        command.Improvements = new List<string>();
        command.Message = string.Empty;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);

        var persisted = await Context.Feedbacks
            .AsNoTracking()
            .AnyAsync(x => x.UserPublicId == UserPublicId);

        Assert.False(persisted);
    }

    [Fact]
    public async Task Handle_WithInvalidExperience_ReturnsBadRequest()
    {
        var command = CreateCommand(experience: "amazing");

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithInvalidImprovement_ReturnsBadRequest()
    {
        var command = CreateCommand(
            improvements: new List<string> { "features", "not-a-real-option" });

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithMessageTooLong_ReturnsBadRequest()
    {
        var command = CreateCommand(message: new string('a', 601));

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    // ---------------------------------------------------------
    // 4. Normalization
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_NormalizesExperienceToLowercase()
    {
        var command = CreateCommand(experience: "  GREAT  ");

        var handler = CreateHandler();
        await handler.Handle(command, default);

        var feedback = await Context.Feedbacks
            .AsNoTracking()
            .FirstAsync(x => x.UserPublicId == UserPublicId);

        Assert.Equal("great", feedback.Experience);
    }

    [Fact]
    public async Task Handle_NormalizesAndDeduplicatesImprovements()
    {
        var command = CreateCommand(
            improvements: new List<string> { "FEATURES", " speed ", "Features" });

        var handler = CreateHandler();
        await handler.Handle(command, default);

        var feedback = await Context.Feedbacks
            .AsNoTracking()
            .FirstAsync(x => x.UserPublicId == UserPublicId);

        Assert.Equal("features,speed", feedback.Improvements);
    }

    [Fact]
    public async Task Handle_FiltersEmptyImprovements()
    {
        var command = CreateCommand(
            improvements: new List<string> { "features", "", "   ", "speed" });

        var handler = CreateHandler();
        await handler.Handle(command, default);

        var feedback = await Context.Feedbacks
            .AsNoTracking()
            .FirstAsync(x => x.UserPublicId == UserPublicId);

        Assert.Equal("features,speed", feedback.Improvements);
    }

    [Fact]
    public async Task Handle_TrimsMessage()
    {
        var command = CreateCommand(message: "   hello world   ");

        var handler = CreateHandler();
        await handler.Handle(command, default);

        var feedback = await Context.Feedbacks
            .AsNoTracking()
            .FirstAsync(x => x.UserPublicId == UserPublicId);

        Assert.Equal("hello world", feedback.Message);
    }

    // ---------------------------------------------------------
    // 5. REGRESSION: notification failures must not fail the request
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillPersistsFeedback()
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
            .Throws(new InvalidOperationException("Redis down"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);

        var persisted = await Context.Feedbacks
            .AsNoTracking()
            .AnyAsync(x => x.UserPublicId == UserPublicId);

        Assert.True(persisted);
    }

    [Fact]
    public async Task Handle_WhenEmailQueueThrows_StillPersistsFeedback()
    {
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
    }

    [Fact]
    public async Task Handle_WhenBothNotificationsThrow_StillPersistsFeedback()
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

        var persisted = await Context.Feedbacks
            .AsNoTracking()
            .AnyAsync(x => x.UserPublicId == UserPublicId);

        Assert.True(persisted);
    }

    [Fact]
    public async Task Handle_WhenBothNotificationsThrow_ReturnsNotificationFalse()
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

        Assert.NotNull(result.Data);
        Assert.False(result.Data!.Notification);
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

    [Fact]
    public async Task Handle_WhenEmailEmpty_SkipsEmailNotification()
    {
        var command = CreateCommand();
        command.Email = string.Empty;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);

        Assert.NotNull(result.Data);
        Assert.True(result.Data!.Notification); // in-app still succeeded
    }

    // ---------------------------------------------------------
    // 6. REGRESSION: notification content
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidRequest_SendsInAppNotificationWithCorrectContent()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.System,
                "Thank you for your feedback",
                It.Is<string>(m =>
                    m.Contains(UserFirstName) &&
                    m.Contains(AcknowledgmentMessage)),
                "/feedback",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SendsEmailNotificationWithCorrectContent()
    {
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m =>
                    m.Contains(AcknowledgmentMessage) &&
                    m.Contains("Your input helps us improve MOVA for everyone.")),
                "We received your feedback"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenFirstNameEmpty_UsesGenericAcknowledgment()
    {
        var command = CreateCommand();
        command.FirstName = string.Empty;

        var handler = CreateHandler();
        await handler.Handle(command, default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.System,
                "Thank you for your feedback",
                AcknowledgmentMessage,   // <-- exactly the generic message
                "/feedback",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}