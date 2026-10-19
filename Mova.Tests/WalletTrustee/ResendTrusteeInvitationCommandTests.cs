// using System.Net;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Options;
// using Moq;
// using Mova.Application.BBL.Commands.AccountWalletTrustee;
// using Mova.Application.Helpers;
// using Mova.Application.Interfaces.Identity;
// using Mova.Application.Interfaces.Notification;
// using Mova.Application.Interfaces.Security;
// using Mova.Domain.Entities;
// using Mova.Domain.Enums;
// using Xunit;

// namespace Mova.Tests.Handlers;

// public sealed class ResendTrusteeInvitationCommandTests : BaseTest
// {
//     private const string OwnerPublicId = "owner_123";
//     private const string OwnerFirstName = "Lucky";

//     private readonly Mock<IIdentityService> _identityService = new();
//     private readonly Mock<INotificationQueue> _notifications = new();
//     private readonly Mock<IPinChallenge> _pinChallenge = new();
//     private readonly Mock<IOptions<TrusteeOptions>> _optionsMock = new();

//     public ResendTrusteeInvitationCommandTests()
//     {
//         _pinChallenge
//             .Setup(x => x.ValidateAsync(OwnerPublicId, PinChallengeType.CreateTrustee, It.IsAny<CancellationToken>()))
//             .ReturnsAsync((true, string.Empty));

//         _optionsMock.Setup(x => x.Value).Returns(new TrusteeOptions
//         {
//             InvitationExpiryHours = 24,
//             ConsentBaseUrl = "https://mova.com/accept"
//         });
//     }

//     private ResendTrusteeInvitationCommand.Handler CreateHandler()
//     {
//         return new ResendTrusteeInvitationCommand.Handler(
//             _identityService.Object,
//             UnitOfWork,
//             Mock.Of<ILogger<ResendTrusteeInvitationCommand.Handler>>(),
//             _notifications.Object,
//             _pinChallenge.Object,
//             _optionsMock.Object);
//     }

//     private ResendTrusteeInvitationCommand.Command CreateCommand(long trusteeId)
//     {
//         return new ResendTrusteeInvitationCommand.Command
//         {
//             UserPublicId = OwnerPublicId,
//             FirstName = OwnerFirstName,
//             TrusteeId = trusteeId,
//         };
//     }

//     private async Task<WalletTrustee> SeedTrusteeAsync(
//         WalletTrusteeStatus status, 
//         string owner = OwnerPublicId)
//     {
//         var trustee = new WalletTrustee
//         {
//             OwnerPublicId = owner,
//             Name = "Jane Doe",
//             Email = "jane@test.com",
//             Status = status,
//             VerificationTokenHash = new byte[] { 1, 2, 3 },
//             TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
//         };
//         await UnitOfWork.AddAsync(trustee);
//         await UnitOfWork.SaveChangesAsync();
//         return trustee;
//     }

//     [Fact]
//     public async Task Handle_WhenPinFails_ReturnsBadRequest()
//     {
//         _pinChallenge
//             .Setup(x => x.ValidateAsync(OwnerPublicId, PinChallengeType.CreateTrustee, It.IsAny<CancellationToken>()))
//             .ReturnsAsync((false, "Invalid PIN"));

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(1), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
//     }

//     [Fact]
//     public async Task Handle_WhenTrusteeNotFound_ReturnsNotFound()
//     {
//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(999), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
//     }

//     [Fact]
//     public async Task Handle_WhenTrusteeBelongsToAnotherUser_ReturnsNotFound()
//     {
//         var trustee = await SeedTrusteeAsync(WalletTrusteeStatus.Pending, owner: "other_owner");

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(trustee.Id), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
//     }

//     [Fact]
//     public async Task Handle_WhenAlreadyVerified_ReturnsConflict()
//     {
//         var trustee = await SeedTrusteeAsync(WalletTrusteeStatus.Verified);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(trustee.Id), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
//         Assert.Contains("already accepted", result.Message);
//     }

//     [Fact]
//     public async Task Handle_WhenPending_GeneratesNewTokenAndEmails()
//     {
//         var trustee = await SeedTrusteeAsync(WalletTrusteeStatus.Pending);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(trustee.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.OK, result.StatusCode);

//         // Verify DB update (using AsNoTracking due to ExecuteUpdateAsync)
//         var updated = await Context.WalletTrustees.AsNoTracking().FirstAsync(x => x.Id == trustee.Id);
//         Assert.NotNull(updated.VerificationTokenHash);
//         Assert.True(updated.TokenExpiresAt > DateTimeOffset.UtcNow.AddHours(23));

//         // Verify Email
//         _notifications.Verify(x => x.QueueNotificationEmail(
//             "Jane Doe",
//             "jane@test.com",
//             It.Is<string>(m => m.Contains("https://mova.com/accept/")),
//             "Lucky resent their trustee invitation on MOVA"), Times.Once);
//     }
// }