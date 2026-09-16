using System.Net;
using Microsoft.EntityFrameworkCore;
using Moq;
using Mova.Application.BBL.MovaAPIs;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class GetNotificationsQueryTests : BaseTest
{
    private const string UserPublicId = "user_notifications_test";
    private const string OtherUserPublicId = "someone_else";

    private GetNotificationsQuery.Handler CreateHandler()
    {
        return new GetNotificationsQuery.Handler(UnitOfWork);
    }

    private GetNotificationsQuery.Query CreateQuery(
        string? userPublicId = null,
        bool unreadOnly = false)
    {
        return new GetNotificationsQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
            UnreadOnly = unreadOnly,
        };
    }

    // ---------------------------------------------------------
    // Seed helper
    // ---------------------------------------------------------

    private async Task<AppNotification> SeedNotificationAsync(
        string userPublicId = UserPublicId,
        string title = "Test Notification",
        string message = "Test message",
        NotificationType type = NotificationType.System,
        bool isRead = false,
        string? actionUrl = null,
        DateTimeOffset? createdAt = null)
    {
        var notification = new AppNotification
        {
            UserPublicId = userPublicId,
            Title = title,
            Message = message,
            Type = type,
            IsRead = isRead,
            ActionUrl = actionUrl,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

        await UnitOfWork.AddAsync(notification);
        await UnitOfWork.SaveChangesAsync();

        return notification;
    }

    // =========================================================
    // 1. Validation
    // =========================================================

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(userPublicId: ""),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User ID is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithWhitespaceUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(userPublicId: "   "),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    // =========================================================
    // 2. Empty result
    // =========================================================

    [Fact]
    public async Task Handle_WithNoNotifications_ReturnsEmptyList()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!);
    }

    // =========================================================
    // 3. Basic retrieval
    // =========================================================

    [Fact]
    public async Task Handle_WithNotifications_ReturnsThem()
    {
        await SeedNotificationAsync(title: "Welcome");
        await SeedNotificationAsync(title: "Deposit received");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task Handle_ReturnsAllFieldsCorrectly()
    {
        await SeedNotificationAsync(
            title: "Deposit successful",
            message: "₦5,000 has been added",
            type: NotificationType.Deposit,
            isRead: false,
            actionUrl: "/add-funds?tab=history");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var dto = Assert.Single(result.Data!);
        Assert.Equal("Deposit successful", dto.Title);
        Assert.Equal("₦5,000 has been added", dto.Message);
        Assert.Equal("Deposit", dto.Type);   // enum → string
        Assert.False(dto.IsRead);
        Assert.Equal("/add-funds?tab=history", dto.ActionUrl);
        Assert.NotEqual(default, dto.CreatedAt);
        Assert.NotEqual(0, dto.Id);
    }

    // =========================================================
    // 4. User scoping (IDOR / isolation)
    // =========================================================

    [Fact]
    public async Task Handle_OnlyReturnsNotificationsForRequestingUser()
    {
        await SeedNotificationAsync(userPublicId: UserPublicId, title: "Mine 1");
        await SeedNotificationAsync(userPublicId: UserPublicId, title: "Mine 2");
        await SeedNotificationAsync(userPublicId: OtherUserPublicId, title: "Not mine");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(2, result.Data!.Count);
        Assert.DoesNotContain(result.Data!, n => n.Title == "Not mine");
    }

    [Fact]
    public async Task Handle_ForUserWithNoNotifications_DoesNotLeakOtherUsersData()
    {
        await SeedNotificationAsync(userPublicId: OtherUserPublicId, title: "Not mine");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!);
    }

    // =========================================================
    // 5. UnreadOnly filter
    // =========================================================

    [Fact]
    public async Task Handle_WithUnreadOnly_ReturnsOnlyUnreadNotifications()
    {
        await SeedNotificationAsync(title: "Unread 1", isRead: false);
        await SeedNotificationAsync(title: "Read 1", isRead: true);
        await SeedNotificationAsync(title: "Unread 2", isRead: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(unreadOnly: true),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data!, n => Assert.False(n.IsRead));
    }

    [Fact]
    public async Task Handle_WithUnreadOnly_WhenAllRead_ReturnsEmpty()
    {
        await SeedNotificationAsync(isRead: true);
        await SeedNotificationAsync(isRead: true);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(unreadOnly: true),
            default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task Handle_WithUnreadFalse_ReturnsReadAndUnread()
    {
        await SeedNotificationAsync(title: "Unread", isRead: false);
        await SeedNotificationAsync(title: "Read", isRead: true);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateQuery(unreadOnly: false),
            default);

        Assert.Equal(2, result.Data!.Count);
    }

    // =========================================================
    // 6. Ordering
    // =========================================================

    [Fact]
    public async Task Handle_OrdersByMostRecentFirst()
    {
        // Seed oldest first, then newer. The handler should return the newest first.
        await SeedNotificationAsync(title: "Oldest");
        await SeedNotificationAsync(title: "Middle");
        await SeedNotificationAsync(title: "Newest");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(3, result.Data!.Count);
        Assert.Equal("Newest", result.Data[0].Title);
        Assert.Equal("Middle", result.Data[1].Title);
        Assert.Equal("Oldest", result.Data[2].Title);
    }

    // =========================================================
    // 7. Take(100) limit
    // =========================================================

    [Fact]
    public async Task Handle_WithMoreThan100Notifications_ReturnsAtMost100()
    {
        for (var i = 0; i < 105; i++)
        {
            await SeedNotificationAsync(title: $"Notification {i}");
        }

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(100, result.Data!.Count);
    }

    [Fact]
    public async Task Handle_WithMoreThan100Notifications_ReturnsNewest100()
    {
        for (var i = 0; i < 105; i++)
        {
            await SeedNotificationAsync(title: $"Notification {i}");
        }

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        // The last seeded is "Notification 104" — newest by Id
        Assert.Equal("Notification 104", result.Data![0].Title);
        // And the 100th in the list should be "Notification 5"
        Assert.Equal("Notification 5", result.Data[99].Title);
    }

    // =========================================================
    // 8. NotificationType serialization
    // =========================================================

    [Theory]
    [InlineData(NotificationType.System, "System")]
    [InlineData(NotificationType.Deposit, "Deposit")]
    [InlineData(NotificationType.Wallet, "Wallet")]
    [InlineData(NotificationType.Security, "Security")]
    public async Task Handle_SerializesTypeAsEnumName(
        NotificationType type,
        string expected)
    {
        await SeedNotificationAsync(type: type);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var dto = Assert.Single(result.Data!);
        Assert.Equal(expected, dto.Type);
    }

    // =========================================================
    // 9. Null ActionUrl
    // =========================================================

    [Fact]
    public async Task Handle_WithNullActionUrl_ReturnsNullActionUrl()
    {
        await SeedNotificationAsync(actionUrl: null);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var dto = Assert.Single(result.Data!);
        Assert.Null(dto.ActionUrl);
    }

    // =========================================================
    // 10. Soft-deleted notifications
    // =========================================================

    [Fact]
    public async Task Handle_ExcludesSoftDeletedNotifications()
    {
        await SeedNotificationAsync(title: "Visible");
        var deleted = await SeedNotificationAsync(title: "Deleted");

        deleted.IsDeleted = true;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Single(result.Data!);
        Assert.Equal("Visible", result.Data![0].Title);
    }
}