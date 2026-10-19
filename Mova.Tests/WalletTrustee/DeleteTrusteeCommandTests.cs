// using System.Net;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using Moq;
// using Mova.Application.BBL.Commands.AccountWalletTrustee;
// using Mova.Application.Interfaces.Identity;
// using Mova.Application.Interfaces.Notification;
// using Mova.Application.Interfaces.Security;
// using Mova.Domain.Entities;
// using Mova.Domain.Enums;
// using Mova.Domain.ValueObjects;
// using Xunit;

// namespace Mova.Tests.Handlers;

// public sealed class DeleteTrusteeCommandTests : BaseTest
// {
//     private const string UserPublicId = "owner_123";
//     private const string UserFirstName = "Lucky";
    
//     private readonly Mock<IIdentityService> _identityService = new();
//     private readonly Mock<INotificationQueue> _notifications = new();
//     private readonly Mock<IPinChallenge> _pinChallenge = new();

//     public DeleteTrusteeCommandTests()
//     {
//         _pinChallenge
//             .Setup(x => x.ValidateAsync(UserPublicId, PinChallengeType.DeleteTrustee, It.IsAny<CancellationToken>()))
//             .ReturnsAsync((true, string.Empty));
//     }

//     private DeleteTrusteeCommand.Handler CreateHandler()
//     {
//         return new DeleteTrusteeCommand.Handler(
//             UnitOfWork,
//             _identityService.Object,
//             _notifications.Object,
//             _pinChallenge.Object,
//             Mock.Of<ILogger<DeleteTrusteeCommand.Handler>>());
//     }

//     private DeleteTrusteeCommand.Command CreateCommand(long trusteeId)
//     {
//         return new DeleteTrusteeCommand.Command
//         {
//             UserPublicId = UserPublicId,
//             FirstName = UserFirstName,
//             TrusteeId = trusteeId,
//         };
//     }

//     private async Task<WalletTrustee> SeedTrusteeAsync(
//         string ownerPublicId = UserPublicId,
//         WalletTrusteeStatus status = WalletTrusteeStatus.Verified,
//         string email = "jane@mova.app")
//     {
//         var trustee = new WalletTrustee
//         {
//             OwnerPublicId = ownerPublicId,
//             Name = "Jane Doe",
//             Email = email,
//             Status = status,
//         };
//         await UnitOfWork.AddAsync(trustee);
//         await UnitOfWork.SaveChangesAsync();
//         return trustee;
//     }

//     private async Task<Wallet> SeedWalletAsync(
//         string ownerPublicId = UserPublicId,
//         long? trusteeId = null,
//         WalletType type = WalletType.Trustee,
//         WalletStatus status = WalletStatus.Active)
//     {
//         var cat = new WalletCategory { Name = "Test", Icon = "Test" };
//         await UnitOfWork.AddAsync(cat);
//         await UnitOfWork.SaveChangesAsync();

//         var wallet = new Wallet
//         {
//             UserPublicId = ownerPublicId,
//             Name = "Test Wallet",
//             CategoryId = cat.Id,
//             WalletTrusteeId = trusteeId,
//             Type = type,
//             Status = status,
//             TargetAmount = Money.FromNaira(1000)
//         };
//         await UnitOfWork.AddAsync(wallet);
//         await UnitOfWork.SaveChangesAsync();
//         return wallet;
//     }

//     private async Task SeedReleaseRequestAsync(long trusteeId, ReleaseRequestStatus status)
//     {
//         // 1. Create a real wallet so the FK constraint is satisfied
//         var wallet = await SeedWalletAsync();

//         // 2. Create the release request pointing to the real wallet
//         var request = new ReleaseRequest
//         {
//             WalletId = wallet.Id,
//             RequestedByUserPublicId = UserPublicId,
//             DecidedByTrusteeId = trusteeId,
//             Status = status,
//             Amount = Money.FromNaira(100),
//             ApprovalTokenHash = new byte[] { 1, 2, 3 },
//             ExpiresAt = DateTimeOffset.UtcNow
//         };
        
//         await UnitOfWork.AddAsync(request);
//         await UnitOfWork.SaveChangesAsync();
//     }

//     [Fact]
//     public async Task Handle_WhenPinFails_ReturnsBadRequest()
//     {
//         _pinChallenge
//             .Setup(x => x.ValidateAsync(UserPublicId, PinChallengeType.DeleteTrustee, It.IsAny<CancellationToken>()))
//             .ReturnsAsync((false, "Invalid PIN"));

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(1), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
//         Assert.Equal("Invalid PIN", result.Message);
//     }

//     [Fact]
//     public async Task Handle_WhenTrusteeNotFound_ReturnsNotFound()
//     {
//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(999), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
//         Assert.Equal("Trustee not found.", result.Message);
//     }

//     [Fact]
//     public async Task Handle_WhenTrusteeBelongsToAnotherUser_ReturnsNotFound()
//     {
//         var trustee = await SeedTrusteeAsync(ownerPublicId: "other_owner");
        
//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(trustee.Id), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
//     }

//     [Fact]
//     public async Task Handle_WhenTrusteeIsLinkedToActiveWallet_ReturnsConflict()
//     {
//         var trustee = await SeedTrusteeAsync();
//         await SeedWalletAsync(trusteeId: trustee.Id, status: WalletStatus.Active);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(trustee.Id), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
//         Assert.Contains("used by 1 active wallet(s)", result.Message);
//         Assert.Single(result.Data!.LinkedWallets!);
//     }

//     [Fact]
//     public async Task Handle_WhenTrusteeHasPastDecisions_ReturnsConflict()
//     {
//         var trustee = await SeedTrusteeAsync();
        
//         // This now seeds a real wallet internally, preventing the FK error
//         await SeedReleaseRequestAsync(trustee.Id, ReleaseRequestStatus.Approved);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(trustee.Id), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
//         Assert.Contains("approved or rejected 1 release request(s)", result.Message);
//         Assert.Equal(1, result.Data!.ReleaseDecisionCount);
//     }

//     [Fact]
//     public async Task Handle_WhenPendingTrustee_DeletesWithoutEmailing()
//     {
//         var trustee = await SeedTrusteeAsync(status: WalletTrusteeStatus.Pending);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(trustee.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("Invitation withdrawn.", result.Message);
        
//         Assert.False(await Context.WalletTrustees.AnyAsync(x => x.Id == trustee.Id));
//         _notifications.VerifyNoOtherCalls();
//     }

//     [Fact]
//     public async Task Handle_WhenVerifiedTrustee_DeletesAndEmails()
//     {
//         var trustee = await SeedTrusteeAsync(status: WalletTrusteeStatus.Verified, email: "jane@test.com");

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(trustee.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("Trustee removed. They have been notified by email.", result.Message);
        
//         Assert.False(await Context.WalletTrustees.AnyAsync(x => x.Id == trustee.Id));

//         _notifications.Verify(x => x.QueueNotificationEmail(
//             "Jane Doe",
//             "jane@test.com",
//             It.Is<string>(m => m.Contains("Lucky") && m.Contains("removed you as their trustee")),
//             "You've been removed as a trustee on MOVA"), Times.Once);
//     }
// }