using System.Net;
using Microsoft.EntityFrameworkCore;
using Mova.Application.BBL.MovaAPIs;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class MarkAllNotificationsAsReadTests : BaseTest
{
    private const string UserPublicId = "user_markall_test";
    private const string OtherUserPublicId = "someone_else";

    private MarkAllNotificationsAsRead.Handler CreateHandler()
    {
        return new MarkAllNotificationsAsRead.Handler(UnitOfWork);
    }

    private MarkAllNotificationsAsRead.Command CreateCommand(
        string? userPublicId = null)
    {
        return new MarkAllNotificationsAsRead.Command
        {
            UserPublicId = userPublicId ?? UserPublicId,
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
        DateTimeOffset? readAt = null,
        string? actionUrl = null)
    {
        var notification = new AppNotification
        {
            UserPublicId = userPublicId,
            Title = title,
            Message = message,
            Type = type,
            IsRead = isRead,
            ReadAt = readAt,
            ActionUrl = actionUrl,
        };

        await UnitOfWork.AddAsync(notification);
        await UnitOfWork.SaveChangesAsync();
        return notification;
    }

    // =========================================================
    // 1. No notifications at all
    // =========================================================

    [Fact]
    public async Task Handle_WithNoNotifications_ReturnsOk()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("No unread notifications.", result.Message);
    }

    [Fact]
    public async Task Handle_WithNoNotifications_DoesNotSaveChanges()
    {
        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    // =========================================================
    // 2. All already read
    // =========================================================

    [Fact]
    public async Task Handle_WithAllAlreadyRead_ReturnsOkWithNoUnreadMessage()
    {
        await SeedNotificationAsync(isRead: true, readAt: DateTimeOffset.UtcNow.AddHours(-1));
        await SeedNotificationAsync(isRead: true, readAt: DateTimeOffset.UtcNow.AddHours(-2));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("No unread notifications.", result.Message);
    }

    [Fact]
    public async Task Handle_WithAllAlreadyRead_DoesNotSaveChanges()
    {
        await SeedNotificationAsync(isRead: true);
        await SeedNotificationAsync(isRead: true);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    // =========================================================
    // 3. Happy path — some unread
    // =========================================================

    [Fact]
    public async Task Handle_WithUnreadNotifications_MarksThemAsRead()
    {
        var n1 = await SeedNotificationAsync(title: "A", isRead: false);
        var n2 = await SeedNotificationAsync(title: "B", isRead: false);
        var n3 = await SeedNotificationAsync(title: "C", isRead: true);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("2 notification(s) marked as read.", result.Message);

        var reloadedN1 = await Context.AppNotifications.AsNoTracking().FirstAsync(x => x.Id == n1.Id);
        var reloadedN2 = await Context.AppNotifications.AsNoTracking().FirstAsync(x => x.Id == n2.Id);
        var reloadedN3 = await Context.AppNotifications.AsNoTracking().FirstAsync(x => x.Id == n3.Id);

        Assert.True(reloadedN1.IsRead);
        Assert.True(reloadedN2.IsRead);
        Assert.NotNull(reloadedN1.ReadAt);
        Assert.NotNull(reloadedN2.ReadAt);
        Assert.True(reloadedN3.IsRead);   // already read, unchanged
    }

    [Fact]
    public async Task Handle_WithOneUnread_MarksItAsRead()
    {
        var notification = await SeedNotificationAsync(isRead: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("1 notification(s) marked as read.", result.Message);

        var reloaded = await Context.AppNotifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == notification.Id);

        Assert.True(reloaded.IsRead);
        Assert.NotNull(reloaded.ReadAt);
    }

    [Fact]
    public async Task Handle_WithUnreadNotifications_SavesChangesOnce()
    {
        await SeedNotificationAsync(isRead: false);
        await SeedNotificationAsync(isRead: false);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WithUnreadNotifications_SetsReadAtToNow()
    {
        var before = DateTimeOffset.UtcNow;
        var notification = await SeedNotificationAsync(isRead: false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);

        var after = DateTimeOffset.UtcNow;

        var reloaded = await Context.AppNotifications
            .AsNoTracking()
            .FirstAsync(x => x.Id == notification.Id);

        Assert.NotNull(reloaded.ReadAt);
        Assert.True(reloaded.ReadAt >= before);
        Assert.True(reloaded.ReadAt <= after);
    }

    // =========================================================
    // 4. User scoping (IDOR / isolation)
    // =========================================================

    [Fact]
    public async Task Handle_DoesNotMarkOtherUsersNotificationsAsRead()
    {
        var mine = await SeedNotificationAsync(userPublicId: UserPublicId, isRead: false);
        var other = await SeedNotificationAsync(userPublicId: OtherUserPublicId, isRead: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("1 notification(s) marked as read.", result.Message);

        var reloadedMine = await Context.AppNotifications.AsNoTracking().FirstAsync(x => x.Id == mine.Id);
        var reloadedOther = await Context.AppNotifications.AsNoTracking().FirstAsync(x => x.Id == other.Id);

        Assert.True(reloadedMine.IsRead);
        Assert.False(reloadedOther.IsRead);   // untouched
    }

    [Fact]
    public async Task Handle_ForUserWithNoUnread_DoesNotTouchOtherUsersData()
    {
        await SeedNotificationAsync(userPublicId: OtherUserPublicId, isRead: false);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("No unread notifications.", result.Message);

        var allOtherUnread = await Context.AppNotifications
            .Where(x => x.UserPublicId == OtherUserPublicId)
            .ToListAsync();

        Assert.All(allOtherUnread, n => Assert.False(n.IsRead));
    }

    // =========================================================
    // 5. Empty UserPublicId (current behavior — no validation)
    // =========================================================

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsOkWithNoUnread()
    {
        // Handler currently has no validation on UserPublicId.
        // It will match no notifications and return "No unread notifications.".
        await SeedNotificationAsync(userPublicId: UserPublicId, isRead: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(userPublicId: ""),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("No unread notifications.", result.Message);
    }

    // =========================================================
    // 6. Soft-deleted notifications
    // =========================================================

    [Fact]
    public async Task Handle_ExcludesSoftDeletedNotifications()
    {
        var visible = await SeedNotificationAsync(title: "Visible", isRead: false);
        var deleted = await SeedNotificationAsync(title: "Deleted", isRead: false);

        deleted.IsDeleted = true;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(), default);

        Assert.Equal("1 notification(s) marked as read.", result.Message);

        var reloadedDeleted = await Context.AppNotifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(x => x.Id == deleted.Id);

        Assert.False(reloadedDeleted.IsRead);   // untouched
    }

    // =========================================================
    // 7. Multiple calls
    // =========================================================

    [Fact]
    public async Task Handle_WhenCalledTwice_SecondCallFindsNoUnread()
    {
        await SeedNotificationAsync(isRead: false);

        var handler = CreateHandler();

        var first = await handler.Handle(CreateCommand(), default);
        Assert.Equal("1 notification(s) marked as read.", first.Message);

        var second = await handler.Handle(CreateCommand(), default);
        Assert.Equal("No unread notifications.", second.Message);
    }

    [Fact]
    public async Task Handle_WhenCalledTwice_OnlySavesChangesOnce()
    {
        await SeedNotificationAsync(isRead: false);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(), default);
        await handler.Handle(CreateCommand(), default);

        // First call saves (1 notification), second call finds nothing to save.
        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }
}