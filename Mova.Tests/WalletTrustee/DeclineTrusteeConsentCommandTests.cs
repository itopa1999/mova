using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.AccountWalletTrustee;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class DeclineTrusteeConsentCommandTests : BaseTest
{
    private const string OwnerPublicId = "user_owner_test";
    private const string OwnerEmail = "owner@mova.app";
    private const string OwnerFirstName = "Lucky";
    private const string TrusteeEmail = "trustee@mova.app";
    private const string TrusteeName = "Jane Doe";

    private readonly Mock<INotificationQueue> _notifications = new();
    private readonly Mock<IIdentityService> _identityService = new();

    private DeclineTrusteeConsentCommand.Handler CreateHandler()
    {
        return new DeclineTrusteeConsentCommand.Handler(
            UnitOfWork,
            _identityService.Object,
            _notifications.Object,
            Mock.Of<ILogger<DeclineTrusteeConsentCommand.Handler>>());
    }

    private DeclineTrusteeConsentCommand.Command CreateCommand(
        string token,
        string? reason = null)
    {
        return new DeclineTrusteeConsentCommand.Command
        {
            Token = token,
            Reason = reason,
        };
    }

    private void SetupOwnerExists(
        string email = OwnerEmail, 
        string firstName = OwnerFirstName)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                OwnerPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                1,
                OwnerPublicId,
                firstName,
                null,
                "Starboy",
                email,
                "08050000000",
                null,
                Money.FromNaira(0),
                string.Empty,
                true,
                true,
                true,
                false,
                string.Empty,
                DateTimeOffset.UtcNow));
    }

    private void SetupOwnerNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                OwnerPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);
    }

    private async Task<WalletTrustee> SeedTrusteeAsync(
        string token,
        WalletTrusteeStatus status = WalletTrusteeStatus.Pending,
        DateTimeOffset? expiresAt = null,
        string ownerPublicId = OwnerPublicId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        var trustee = new WalletTrustee
        {
            OwnerPublicId = ownerPublicId,
            Name = TrusteeName,
            Email = TrusteeEmail,
            Status = status,
            VerificationTokenHash = hash,
            TokenExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(24),
        };

        await UnitOfWork.AddAsync(trustee);
        await UnitOfWork.SaveChangesAsync();

        return trustee;
    }

    // ---------------------------------------------------------
    // 1. Happy path
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithValidToken_DeletesTrusteeAndReturnsSuccess()
    {
        var token = "valid-token-12345678901234567890";
        var trustee = await SeedTrusteeAsync(token);
        SetupOwnerExists();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Invitation declined. The wallet owner will be notified.", result.Message);

        // Verify physical deletion
        var exists = await Context.WalletTrustees
            .AsNoTracking()
            .AnyAsync(x => x.Id == trustee.Id);
            
        Assert.False(exists);
    }

    [Fact]
    public async Task Handle_WithReason_SendsNotificationsIncludingReason()
    {
        var token = "valid-token-12345678901234567890";
        await SeedTrusteeAsync(token);
        SetupOwnerExists();

        var reason = "I am not comfortable with this.";
        var handler = CreateHandler();
        await handler.Handle(CreateCommand(token, reason), default);

        // Verify In-App
        _notifications.Verify(
            x => x.InAppNotificationAsync(
                OwnerPublicId,
                NotificationType.Wallet,
                "Trustee invitation declined",
                It.Is<string>(m => m.Contains(TrusteeName) && m.Contains(reason)),
                "/dashboard/trustees",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify Email
        _notifications.Verify(
            x => x.QueueNotificationEmail(
                OwnerFirstName,
                OwnerEmail,
                It.Is<string>(m => m.Contains(TrusteeName) && m.Contains(reason)),
                "Trustee invitation declined"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithoutReason_SendsNotificationsWithoutReasonClause()
    {
        var token = "valid-token-12345678901234567890";
        await SeedTrusteeAsync(token);
        SetupOwnerExists();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(token, reason: null), default);

        // Verify In-App does NOT contain "They said:"
        _notifications.Verify(
            x => x.InAppNotificationAsync(
                OwnerPublicId,
                NotificationType.Wallet,
                "Trustee invitation declined",
                It.Is<string>(m => m.Contains(TrusteeName) && !m.Contains("They said:")),
                "/dashboard/trustees",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify Email does NOT contain "They said:"
        _notifications.Verify(
            x => x.QueueNotificationEmail(
                OwnerFirstName,
                OwnerEmail,
                It.Is<string>(m => m.Contains(TrusteeName) && !m.Contains("They said:")),
                "Trustee invitation declined"),
            Times.Once);
    }

    // ---------------------------------------------------------
    // 2. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithInvalidToken_ReturnsNotFound()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand("wrong-token-wrong-token-wrong-token"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("This invitation link is invalid or has already been used.", result.Message);
        
        // Ensure no notifications are sent
        _notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenAlreadyAccepted_ReturnsGone()
    {
        var token = "valid-token-12345678901234567890";
        await SeedTrusteeAsync(token, status: WalletTrusteeStatus.Verified);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
        Assert.Equal("This invitation has already been accepted and cannot be declined.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenTokenExpired_ReturnsGone()
    {
        var token = "valid-token-12345678901234567890";
        await SeedTrusteeAsync(token, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
        Assert.Contains("This invitation has expired", result.Message);
    }

    [Fact]
    public async Task Handle_WhenTokenExpiryIsNull_ReturnsGone()
    {
        var token = "valid-token-12345678901234567890";
        
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var trustee = new WalletTrustee
        {
            OwnerPublicId = OwnerPublicId,
            Name = TrusteeName,
            Email = TrusteeEmail,
            Status = WalletTrusteeStatus.Pending,
            VerificationTokenHash = hash,
            TokenExpiresAt = null,
        };
        await UnitOfWork.AddAsync(trustee);
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
        Assert.Contains("This invitation has expired", result.Message);
    }

    // ---------------------------------------------------------
    // 3. Edge cases
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WhenOwnerNotFound_SkipsEmailButStillSucceeds()
    {
        var token = "valid-token-12345678901234567890";
        await SeedTrusteeAsync(token);
        
        // Owner missing
        SetupOwnerNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.True(result.IsSuccess);

        // In-app still goes
        _notifications.Verify(
            x => x.InAppNotificationAsync(
                It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string>(), 
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Email does NOT
        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOwnerHasNoEmail_SkipsEmailButStillSucceeds()
    {
        var token = "valid-token-12345678901234567890";
        await SeedTrusteeAsync(token);
        
        // Owner exists but no email
        SetupOwnerExists(email: string.Empty);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.True(result.IsSuccess);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}