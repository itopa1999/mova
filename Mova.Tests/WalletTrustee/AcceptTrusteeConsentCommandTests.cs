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

public sealed class AcceptTrusteeConsentCommandTests : BaseTest
{
    private const string OwnerPublicId = "user_owner_test";
    private const string OwnerEmail = "owner@mova.app";
    private const string OwnerFirstName = "Lucky";
    private const string TrusteeEmail = "trustee@mova.app";
    private const string TrusteeName = "Jane Doe";

    private readonly Mock<INotificationQueue> _notifications = new();
    private readonly Mock<IIdentityService> _identityService = new();

    private AcceptTrusteeConsentCommand.Handler CreateHandler()
    {
        return new AcceptTrusteeConsentCommand.Handler(
            UnitOfWork,
            _identityService.Object,
            _notifications.Object,
            Mock.Of<ILogger<AcceptTrusteeConsentCommand.Handler>>());
    }

    private AcceptTrusteeConsentCommand.Command CreateCommand(
        string token,
        string? ipAddress = "192.168.1.1")
    {
        return new AcceptTrusteeConsentCommand.Command
        {
            Token = token,
            IpAddress = ipAddress,
            UserAgent = "Mozilla/5.0 Test"
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
    public async Task Handle_WithValidToken_VerifiesTrusteeAndClearsToken()
    {
        var token = "valid-token-12345678901234567890";
        var trustee = await SeedTrusteeAsync(token);
        SetupOwnerExists();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var updated = await Context.WalletTrustees
            .AsNoTracking()
            .FirstAsync(x => x.Id == trustee.Id);

        Assert.Equal(WalletTrusteeStatus.Verified, updated.Status);
        Assert.NotNull(updated.VerifiedAt);
        Assert.Null(updated.VerificationTokenHash);
        Assert.Null(updated.TokenExpiresAt);
        Assert.Equal("192.168.1.1", updated.ConsentedFromIp);
    }

    [Fact]
    public async Task Handle_WithValidToken_SendsNotificationsToOwner()
    {
        var token = "valid-token-12345678901234567890";
        await SeedTrusteeAsync(token);
        SetupOwnerExists();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(token), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                OwnerPublicId,
                NotificationType.Wallet,
                "Trustee accepted your invitation",
                It.Is<string>(m => m.Contains(TrusteeName) && m.Contains("accepted to be your trustee")),
                "/dashboard/trustees",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                OwnerFirstName,
                OwnerEmail,
                It.Is<string>(m => m.Contains(TrusteeName) && m.Contains("accepted to be your trustee")),
                "Your trustee accepted the invitation"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TruncatesLongIpAddress()
    {
        var token = "valid-token-12345678901234567890";
        var trustee = await SeedTrusteeAsync(token);
        SetupOwnerExists();

        var longIp = "2001:0db8:85a3:0000:0000:8a2e:0370:7334-some-extra-junk-here";

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(token, longIp), default);

        var updated = await Context.WalletTrustees
            .AsNoTracking()
            .FirstAsync(x => x.Id == trustee.Id);

        Assert.NotNull(updated.ConsentedFromIp);
        Assert.Equal(45, updated.ConsentedFromIp.Length);
    }

    // ---------------------------------------------------------
    // 2. Validation failures
    // ---------------------------------------------------------

    [Fact]
    public async Task Handle_WithInvalidToken_ReturnsNotFound()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand("completely-wrong-token"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("This invitation link is invalid or has already been used.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenAlreadyVerified_ReturnsGone()
    {
        var token = "valid-token-12345678901234567890";
        await SeedTrusteeAsync(token, status: WalletTrusteeStatus.Verified);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
        Assert.Equal("This invitation has already been accepted.", result.Message);
    }

    [Fact]
    public async Task Handle_WhenTokenExpired_ReturnsGone()
    {
        var token = "valid-token-12345678901234567890";
        var expiredAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        
        await SeedTrusteeAsync(token, expiresAt: expiredAt);

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
        
        SetupOwnerNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.True(result.IsSuccess);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenOwnerHasNoEmail_SkipsEmailButStillSucceeds()
    {
        var token = "valid-token-12345678901234567890";
        await SeedTrusteeAsync(token);
        
        SetupOwnerExists(email: string.Empty); 

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(token), default);

        Assert.True(result.IsSuccess);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }
}