using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Mova.Application.BBL.Commands.AccountWalletTrustee;
using Mova.Application.Helpers;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Security;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class InviteTrusteeCommandTests : BaseTest
{
    private const string UserPublicId = "owner_123";
    private const string UserEmail = "owner@mova.app";
    private const string UserFirstName = "Lucky";
    
    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<INotificationQueue> _notifications = new();
    private readonly Mock<IPinChallenge> _pinChallenge = new();
    private readonly Mock<IOptions<TrusteeOptions>> _optionsMock = new();

    public InviteTrusteeCommandTests()
    {
        _pinChallenge
            .Setup(x => x.ValidateAsync(UserPublicId, PinChallengeType.CreateTrustee, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));

        _optionsMock.Setup(x => x.Value).Returns(new TrusteeOptions
        {
            InvitationExpiryHours = 24,
            ConsentBaseUrl = "https://mova.com/accept"
        });
    }

    private InviteTrusteeCommand.Handler CreateHandler()
    {
        return new InviteTrusteeCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            Mock.Of<ILogger<InviteTrusteeCommand.Handler>>(),
            _notifications.Object,
            _pinChallenge.Object,
            _optionsMock.Object);
    }

    private InviteTrusteeCommand.Command CreateCommand(string trusteeEmail, string? name = null)
    {
        return new InviteTrusteeCommand.Command
        {
            UserPublicId = UserPublicId,
            Email = UserEmail,
            FirstName = UserFirstName,
            TrusteeEmail = trusteeEmail,
            TrusteeName = name
        };
    }

    private async Task<WalletTrustee> SeedTrusteeAsync(
        string email, 
        WalletTrusteeStatus status, 
        string ownerPublicId = UserPublicId)
    {
        var trustee = new WalletTrustee
        {
            OwnerPublicId = ownerPublicId,
            Email = email,
            Status = status,
        };
        await UnitOfWork.AddAsync(trustee);
        await UnitOfWork.SaveChangesAsync();
        return trustee;
    }

    [Fact]
    public async Task Handle_WhenPinFails_ReturnsBadRequest()
    {
        _pinChallenge
            .Setup(x => x.ValidateAsync(UserPublicId, PinChallengeType.CreateTrustee, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, "Invalid PIN"));

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand("jane@test.com"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WhenEmailEmpty_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand("   "), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Please provide the email", result.Message);
    }

    [Fact]
    public async Task Handle_WhenInvitingSelf_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand(UserEmail), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("cannot be your own trustee", result.Message);
    }

    [Fact]
    public async Task Handle_WhenTrusteeAlreadyVerified_ReturnsConflict()
    {
        await SeedTrusteeAsync("jane@test.com", WalletTrusteeStatus.Verified);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand("jane@test.com"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Contains("already have this person as a verified trustee", result.Message);
    }

    [Fact]
    public async Task Handle_WhenInviteAlreadyPending_ReturnsConflict()
    {
        await SeedTrusteeAsync("jane@test.com", WalletTrusteeStatus.Pending);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand("jane@test.com"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Contains("An invitation is already pending", result.Message);
    }

    [Fact]
    public async Task Handle_WithValidData_CreatesTrusteeAndSendsEmail()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateCommand("jane@test.com", "Jane Doe"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);

        var trustee = await Context.WalletTrustees.FirstAsync(x => x.Email == "jane@test.com");
        Assert.Equal(WalletTrusteeStatus.Pending, trustee.Status);
        Assert.Equal("Jane Doe", trustee.Name);
        Assert.NotNull(trustee.VerificationTokenHash);

        _notifications.Verify(x => x.QueueNotificationEmail(
            "Jane Doe",
            "jane@test.com",
            It.Is<string>(m => m.Contains("https://mova.com/accept/")),
            "Lucky invited you to be a trustee on MOVA"), Times.Once);
    }
}