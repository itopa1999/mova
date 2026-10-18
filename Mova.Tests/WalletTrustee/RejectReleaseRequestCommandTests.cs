// using System.Net;
// using System.Security.Cryptography;
// using System.Text;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using Moq;
// using Mova.Application.BBL.Commands.AccountWalletTrustee;
// using Mova.Application.Interfaces.Identity;
// using Mova.Application.Interfaces.Notification;
// using Mova.Domain.Entities;
// using Mova.Domain.Enums;
// using Mova.Domain.ValueObjects;
// using Xunit;

// namespace Mova.Tests.Handlers;

// public sealed class RejectReleaseRequestCommandTests : BaseTest
// {
//     private const string OwnerPublicId = "owner_123";
//     private const string OwnerEmail = "owner@mova.app";
//     private const string OwnerFirstName = "Lucky";
//     private const string Token = "valid-token-12345678901234567890";

//     private readonly Mock<IIdentityService> _identityService = new();
//     private readonly Mock<INotificationQueue> _notifications = new();

//     private RejectReleaseRequestCommand.Handler CreateHandler()
//     {
//         return new RejectReleaseRequestCommand.Handler(
//             UnitOfWork,
//             _identityService.Object,
//             _notifications.Object,
//             Mock.Of<ILogger<RejectReleaseRequestCommand.Handler>>());
//     }

//     private RejectReleaseRequestCommand.Command CreateCommand(string? reason = null)
//     {
//         return new RejectReleaseRequestCommand.Command
//         {
//             Token = Token,
//             Reason = reason,
//         };
//     }

//     private async Task<ReleaseRequest> SeedRequestAsync(
//         ReleaseRequestStatus status = ReleaseRequestStatus.Pending,
//         DateTimeOffset? expiresAt = null)
//     {
//         var cat = new WalletCategory { Name = "Test", Icon = "Test" };
//         await UnitOfWork.AddAsync(cat);
        
//         var wallet = new Wallet
//         {
//             UserPublicId = OwnerPublicId,
//             Name = "Rent",
//             CategoryId = cat.Id,
//             LockedAmount = Money.FromNaira(500m),
//             TargetAmount = Money.FromNaira(1000m),
//             Type = WalletType.Trustee,
//             Status = WalletStatus.Active
//         };
//         await UnitOfWork.AddAsync(wallet);
//         await UnitOfWork.SaveChangesAsync();

//         var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Token));
        
//         var request = new ReleaseRequest
//         {
//             WalletId = wallet.Id,
//             RequestedByUserPublicId = OwnerPublicId,
//             Amount = Money.FromNaira(500m),
//             Status = status,
//             ApprovalTokenHash = hash,
//             ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(24),
//         };
//         await UnitOfWork.AddAsync(request);
//         await UnitOfWork.SaveChangesAsync();
//         return request;
//     }

//     private void SetupOwner(string email = OwnerEmail)
//     {
//         _identityService
//             .Setup(x => x.GetByIdentifierAsync(OwnerPublicId, It.IsAny<CancellationToken>()))
//             .ReturnsAsync(new UserIdentityDto(
//                 1, OwnerPublicId, OwnerFirstName, null, "Starboy", 
//                 email, "080", null, Money.FromNaira(0), "", 
//                 true, true, true, false, "", DateTimeOffset.UtcNow));
//     }

//     [Fact]
//     public async Task Handle_WhenInvalidToken_ReturnsNotFound()
//     {
//         var handler = CreateHandler();
//         var result = await handler.Handle(
//             new RejectReleaseRequestCommand.Command { Token = "wrong-token-wrong-token-wrong-token" }, 
//             default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
//     }

//     [Fact]
//     public async Task Handle_WhenAlreadyDecided_ReturnsGone()
//     {
//         await SeedRequestAsync(status: ReleaseRequestStatus.Approved);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
//     }

//     [Fact]
//     public async Task Handle_WhenExpired_ReturnsGone()
//     {
//         await SeedRequestAsync(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand(), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.Gone, result.StatusCode);
//     }

//     [Fact]
//     public async Task Handle_WhenValid_RejectsAndRestoresFunds()
//     {
//         var request = await SeedRequestAsync();
//         SetupOwner();

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateCommand("Not comfortable"), default);

//         Assert.True(result.IsSuccess);

//         // Verify Request flipped
//         var updatedRequest = await Context.ReleaseRequests.AsNoTracking().FirstAsync(x => x.Id == request.Id);
//         Assert.Equal(ReleaseRequestStatus.Rejected, updatedRequest.Status);
//         Assert.Equal("Not comfortable", updatedRequest.DecisionReason);

//         // Verify Wallet LockedAmount restored
//         var wallet = await Context.Wallets.AsNoTracking().FirstAsync(x => x.Id == request.WalletId);
//         Assert.Equal(1000m, wallet.LockedAmount.ToDecimal()); // Was 500, +500 restored

//         // Verify Notification
//         _notifications.Verify(x => x.QueueNotificationEmail(
//             OwnerFirstName, OwnerEmail,
//             It.Is<string>(m => m.Contains("Not comfortable") && m.Contains("funds are back")),
//             "Release rejected"), Times.Once);
//     }
// }