using System.Net;
using Microsoft.EntityFrameworkCore;
using Mova.Application.BBL.MovaAPIs;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class MarkNotificationAsReadTests : BaseTest
{
    private const string UserPublicId = "user_markone_test";
    private const string OtherUserPublicId = "someone_else";

    private MarkNotificationAsRead.Handler CreateHandler()
    {
        return new MarkNotificationAsRead.Handler(UnitOfWork);
    }

    private MarkNotificationAsRead.Command CreateCommand(
        long notificationId,
        string? userPublicId = null)
    {
        return new MarkNotificationAsRead.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
            NotificationId = notificationId,
        };
    }

    // ---------------------------------------------------------
    // Seed helper
    // ---------------------------------------------------------

    private async Task<AppNotification> SeedNotificationAsync(
        string userPublicId = UserPublicId,
        string title = "Test",
        string message = "Test message",
        NotificationType type = NotificationType.System,
        bool isRead = false,
        DateTimeOffset? readAt = null)
    {
        var notification = new AppNotification
        {
            UserPublicId = userPublicId,
            Title = title,
            Message = message,
            Type = type,
            IsRead = isRead,
            ReadAt = readAt,
        };

        await UnitOfWork.AddAsync(notification);
        await UnitOfWork.SaveChangesAsync();
        return notification;
    }

    // =========================================================
    // 1. Not found
    // =========================================================

    [Fact]
    public async Task Handle_WithUnknownId_ReturnsNotFound()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(notificationId: 999_999),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Notification not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownId_DoesNotSaveChanges()
    {
        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(notificationId: 999_999),
            default);

        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    // =========================================================
    // 2. Belongs to another user (IDOR)
    // =========================================================

    [Fact]
    public async Task Handle_WithNotificationBelongingToAnotherUser_ReturnsNotFound()
    {
        var other = await SeedNotificationAsync(userPublicId: OtherUserPublicId);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(notificationId: other.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Notification not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithNotificationBelongingToAnotherUser_DoesNotMarkItRead()
    {
        var other = await SeedNotificationAsync(userPublicId: OtherUserPublicId, isRead: false);

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(notificationId: other.Id),
            default);

        var reloaded = await Context.AppNotifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == other.Id);

        Assert.False(reloaded.IsRead);
        Assert.Null(reloaded.ReadAt);
    }

    // =========================================================
    // 3. Already read (idempotent)
    // =========================================================

    [Fact]
    public async Task Handle_WhenAlreadyRead_ReturnsOkAndDoesNotSave()
    {
        var originalReadAt = DateTimeOffset.UtcNow.AddHours(-1);
        var notification = await SeedNotificationAsync(
            isRead: true,
            readAt: originalReadAt);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(notification.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Notification marked as read.", result.Message);
        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WhenAlreadyRead_DoesNotChangeReadAt()
    {
        var originalReadAt = DateTimeOffset.UtcNow.AddHours(-1);
        var notification = await SeedNotificationAsync(
            isRead: true,
            readAt: originalReadAt);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(notification.Id), default);

        var reloaded = await Context.AppNotifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == notification.Id);

        Assert.True(reloaded.IsRead);
        Assert.Equal(originalReadAt, reloaded.ReadAt);
    }

    // =========================================================
    // 4. Happy path
    // =========================================================

    [Fact]
    public async Task Handle_WithUnreadNotification_MarksItRead()
    {
        var notification = await SeedNotificationAsync(isRead: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(notification.Id),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Notification marked as read.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnreadNotification_PersistsChange()
    {
        var notification = await SeedNotificationAsync(isRead: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(notification.Id), default);

        var reloaded = await Context.AppNotifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == notification.Id);

        Assert.True(reloaded.IsRead);
        Assert.NotNull(reloaded.ReadAt);
    }

    [Fact]
    public async Task Handle_WithUnreadNotification_SavesChangesOnce()
    {
        var notification = await SeedNotificationAsync(isRead: false);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(notification.Id), default);

        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WithUnreadNotification_SetsReadAtToNow()
    {
        var notification = await SeedNotificationAsync(isRead: false);
        var before = DateTimeOffset.UtcNow;

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(notification.Id), default);

        var after = DateTimeOffset.UtcNow;

        var reloaded = await Context.AppNotifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == notification.Id);

        Assert.NotNull(reloaded.ReadAt);
        Assert.True(reloaded.ReadAt >= before);
        Assert.True(reloaded.ReadAt <= after);
    }

    // =========================================================
    // 5. Only the targeted notification is touched
    // =========================================================

    [Fact]
    public async Task Handle_OnlyMarksTheTargetedNotification()
    {
        var target = await SeedNotificationAsync(title: "Target", isRead: false);
        var other = await SeedNotificationAsync(title: "Other", isRead: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(target.Id), default);

        var reloadedTarget = await Context.AppNotifications.AsNoTracking().FirstAsync(x => x.Id == target.Id);
        var reloadedOther = await Context.AppNotifications.AsNoTracking().FirstAsync(x => x.Id == other.Id);

        Assert.True(reloadedTarget.IsRead);
        Assert.False(reloadedOther.IsRead);
    }

    // =========================================================
    // 6. Soft-deleted notification
    // =========================================================

    [Fact]
    public async Task Handle_WithSoftDeletedNotification_ReturnsNotFound()
    {
        var notification = await SeedNotificationAsync(isRead: false);

        notification.IsDeleted = true;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(notification.Id),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Notification not found.", result.Message);
    }

    // =========================================================
    // 7. Repeat calls
    // =========================================================

    [Fact]
    public async Task Handle_WhenCalledTwice_SecondCallDoesNotSaveAgain()
    {
        var notification = await SeedNotificationAsync(isRead: false);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();

        var first = await handler.Handle(CreateCommand(notification.Id), default);
        Assert.True(first.IsSuccess);

        var second = await handler.Handle(CreateCommand(notification.Id), default);
        Assert.True(second.IsSuccess);

        // First call saved once (marking read), second found it already read.
        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }
}