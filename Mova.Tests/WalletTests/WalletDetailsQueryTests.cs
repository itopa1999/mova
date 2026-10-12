// using MediatR;
// using Microsoft.EntityFrameworkCore;
// using Moq;
// using Mova.Application.BBL.Queries.AccountWallet;
// using Mova.Application.Interfaces.Identity;
// using Mova.Domain.Entities;
// using Mova.Domain.Enums;
// using Mova.Domain.ValueObjects;
// using Mova.Shared.Common;
// using System.Net;
// using System.Text.Json;
// using Xunit;

// namespace Mova.Tests.Handlers;

// public sealed class WalletDetailsQueryTests : BaseTest
// {
//     private const string UserPublicId = "user_wd_test";
//     private const string OtherUserPublicId = "someone_else";
//     private const string UserEmail = "user@mova.app";
//     private const string UserFirstName = "Lucky";

//     private readonly Mock<IIdentityService> _identityService = new();
//     private readonly Mock<IMediator> _mediator = new();

//     private WalletDetails.Handler CreateHandler()
//     {
//         return new WalletDetails.Handler(
//             UnitOfWork,
//             _identityService.Object,
//             _mediator.Object);
//     }

//     private WalletDetails.Query CreateQuery(long walletId)
//     {
//         return new WalletDetails.Query
//         {
//             UserPublicId = UserPublicId,
//             WalletId = walletId,
//         };
//     }

//     // ---------------------------------------------------------
//     // Mock setups
//     // ---------------------------------------------------------

//     private void SetupUserExists(string publicId = UserPublicId)
//     {
//         _identityService
//             .Setup(x => x.GetByIdentifierAsync(
//                 It.IsAny<string>(),
//                 It.IsAny<CancellationToken>()))
//             .ReturnsAsync(new UserIdentityDto(
//                 1,
//                 publicId,
//                 UserFirstName,
//                 null,
//                 "Starboy",
//                 UserEmail,
//                 "08050000000",
//                 null,
//                 Money.FromNaira(0),
//                 string.Empty,
//                 true,
//                 true,
//                 true,
//                 false,
//                 "Lucky Starboy",
//                 DateTimeOffset.UtcNow));
//     }

//     private void SetupUserNotFound()
//     {
//         _identityService
//             .Setup(x => x.GetByIdentifierAsync(
//                 It.IsAny<string>(),
//                 It.IsAny<CancellationToken>()))
//             .ReturnsAsync((UserIdentityDto?)null);
//     }

//     private void SetupSchedulePreview(
//         List<GetWalletSchedulePreviewQuery.ReleaseDto>? releases = null,
//         bool isSuccess = true,
//         string? errorMessage = null)
//     {
//         var dto = new GetWalletSchedulePreviewQuery.ReleaseDto
//         {
//             Releases = releases ?? new List<GetWalletSchedulePreviewQuery.ReleaseDto>(),
//         };

//         _mediator
//             .Setup(x => x.Send(
//                 It.IsAny<GetWalletSchedulePreviewQuery.Query>(),
//                 It.IsAny<CancellationToken>()))
//             .ReturnsAsync(new BaseResult<GetWalletSchedulePreviewQuery.ResponseDto>(
//                 isSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
//                 errorMessage ?? (isSuccess ? "OK" : "Failed"),
//                 isSuccess ? dto : null));
//     }

//     // ---------------------------------------------------------
//     // Seed helpers
//     // ---------------------------------------------------------

//     private async Task<WalletCategory> SeedCategoryAsync(
//         string name = "Rent & Housing",
//         string icon = "Wallet")
//     {
//         var category = new WalletCategory { Name = name, Icon = icon };
//         await UnitOfWork.AddAsync(category);
//         await UnitOfWork.SaveChangesAsync();
//         return category;
//     }

//     private async Task<Wallet> SeedWalletAsync(
//         long categoryId,
//         string userPublicId = UserPublicId,
//         string name = "Rent Savings",
//         string? description = "Monthly rent",
//         PayoutDestination destination = PayoutDestination.Bank,
//         long? bankAccountId = null,
//         decimal targetNaira = 30_000m,
//         decimal lockedNaira = 30_000m,
//         decimal availableNaira = 0m,
//         decimal totalReleasedNaira = 0m,
//         decimal totalWithdrawnNaira = 0m,
//         decimal resetNaira = 0m,
//         WalletStatus status = WalletStatus.Active,
//         int restartCount = 0,
//         UtilityType? utilityType = null,
//         string? utilityConfigJson = null)
//     {
//         var wallet = new Wallet
//         {
//             UserPublicId = userPublicId,
//             CategoryId = categoryId,
//             BankAccountId = bankAccountId,
//             Name = name,
//             Description = description,
//             TargetAmount = Money.FromNaira(targetNaira),
//             FundedAmount = Money.FromNaira(targetNaira),
//             LockedAmount = Money.FromNaira(lockedNaira),
//             AvailableAmount = Money.FromNaira(availableNaira),
//             TotalReleasedAmount = Money.FromNaira(totalReleasedNaira),
//             TotalWithdrawnAmount = Money.FromNaira(totalWithdrawnNaira),
//             ResetAmount = Money.FromNaira(resetNaira),
//             PayoutDestination = destination,
//             Status = status,
//             RestartCount = restartCount,
//             UtilityType = utilityType,
//             UtilityConfigJson = utilityConfigJson,
//         };

//         await UnitOfWork.AddAsync(wallet);
//         await UnitOfWork.SaveChangesAsync();
//         return wallet;
//     }

//     private async Task<WalletRule> SeedRuleAsync(
//         long walletId,
//         decimal amountNaira = 1_000m,
//         ReleaseFrequency frequency = ReleaseFrequency.Daily,
//         string config = "{\"type\":\"daily\"}",
//         DateTimeOffset? startDate = null,
//         DateTimeOffset? endDate = null)
//     {
//         var start = startDate ?? DateTimeOffset.UtcNow.Date.AddDays(-5);

//         var rule = new WalletRule
//         {
//             WalletId = walletId,
//             Amount = Money.FromNaira(amountNaira),
//             Frequency = frequency,
//             FrequencyConfig = config,
//             StartDate = start,
//             EndDate = endDate ?? start.AddDays(30),
//         };

//         await UnitOfWork.AddAsync(rule);
//         await UnitOfWork.SaveChangesAsync();
//         return rule;
//     }

//     private async Task<ScheduledRelease> SeedReleaseAsync(
//         long walletId,
//         long ruleId,
//         decimal amountNaira,
//         DateTimeOffset scheduledFor,
//         ReleaseStatus status = ReleaseStatus.Scheduled,
//         DateTimeOffset? releasedAt = null)
//     {
//         var release = new ScheduledRelease
//         {
//             WalletId = walletId,
//             WalletRuleId = ruleId,
//             Amount = Money.FromNaira(amountNaira),
//             ScheduledFor = scheduledFor,
//             Status = status,
//             ReleasedAt = releasedAt ?? (status == ReleaseStatus.Released ? scheduledFor : null),
//         };

//         await UnitOfWork.AddAsync(release);
//         await UnitOfWork.SaveChangesAsync();
//         return release;
//     }

//     private async Task<RenewalPolicy> SeedRenewalPolicyAsync(
//         long walletId,
//         RenewalStatus status = RenewalStatus.Active,
//         RenewalTriggerType triggerType = RenewalTriggerType.OnCompletion,
//         RefillAmountType refillAmountType = RefillAmountType.Fixed,
//         decimal refillNaira = 30_000m)
//     {
//         var policy = new RenewalPolicy
//         {
//             WalletId = walletId,
//             Status = status,
//             TriggerType = triggerType,
//             RefillAmountType = refillAmountType,
//             RefillAmount = Money.FromNaira(refillNaira),
//         };

//         await UnitOfWork.AddAsync(policy);
//         await UnitOfWork.SaveChangesAsync();
//         return policy;
//     }

//     // =========================================================
//     // 1. Validation
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
//     {
//         var handler = CreateHandler();
//         var query = CreateQuery(1);
//         query.UserPublicId = "";

//         var result = await handler.Handle(query, default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
//         Assert.Equal("User public ID is required.", result.Message);
//     }

//     [Fact]
//     public async Task Handle_WithZeroWalletId_ReturnsBadRequest()
//     {
//         SetupUserExists();
//         var handler = CreateHandler();

//         var result = await handler.Handle(CreateQuery(0), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
//         Assert.Equal("Invalid wallet ID.", result.Message);
//     }

//     [Fact]
//     public async Task Handle_WithNegativeWalletId_ReturnsBadRequest()
//     {
//         SetupUserExists();
//         var handler = CreateHandler();

//         var result = await handler.Handle(CreateQuery(-5), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
//     }

//     [Fact]
//     public async Task Handle_WithInvalidWalletId_DoesNotCheckUser()
//     {
//         var handler = CreateHandler();
//         await handler.Handle(CreateQuery(0), default);

//         _identityService.Verify(
//             x => x.GetByIdentifierAsync(
//                 It.IsAny<string>(),
//                 It.IsAny<CancellationToken>()),
//             Times.Never);
//     }

//     // =========================================================
//     // 2. User lookup
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithUnknownUser_ReturnsNotFound()
//     {
//         SetupUserNotFound();
//         var handler = CreateHandler();

//         var result = await handler.Handle(CreateQuery(1), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
//         Assert.Equal("User not found.", result.Message);
//     }

//     // =========================================================
//     // 3. Wallet lookup
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithUnknownWallet_ReturnsNotFound()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var handler = CreateHandler();

//         var result = await handler.Handle(CreateQuery(999_999L), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
//         Assert.Equal("Wallet not found.", result.Message);
//     }

//     [Fact]
//     public async Task Handle_WithWalletBelongingToAnotherUser_ReturnsNotFound()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var otherWallet = await SeedWalletAsync(
//             category.Id,
//             userPublicId: OtherUserPublicId);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(otherWallet.Id), default);

//         Assert.False(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
//     }

//     // =========================================================
//     // 4. Happy path — Bank
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithBankWallet_ReturnsSuccess()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id, destination: PayoutDestination.Bank);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal(HttpStatusCode.OK, result.StatusCode);
//         Assert.NotNull(result.Data);
//         Assert.Equal(wallet.Id, result.Data!.WalletId);
//         Assert.Equal("Rent Savings", result.Data.Name);
//     }

//     [Fact]
//     public async Task Handle_WithBankWallet_PayoutDestinationIsLowercaseBank()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id, destination: PayoutDestination.Bank);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Equal("bank", result.Data!.PayoutDestination);
//     }

//     [Fact]
//     public async Task Handle_WithBankWallet_UtilityFieldsAreNull()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id, destination: PayoutDestination.Bank);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Null(result.Data!.UtilityType);
//         Assert.Null(result.Data.UtilityConfig);
//         Assert.Null(result.Data.UtilitySummary);
//     }

//     // =========================================================
//     // 5. Happy path — Wallet
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithWalletDestination_ReturnsSuccess()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id, destination: PayoutDestination.Wallet);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("wallet", result.Data!.PayoutDestination);
//         Assert.Null(result.Data.UtilityType);
//         Assert.Null(result.Data.UtilityConfig);
//         Assert.Null(result.Data.UtilitySummary);
//     }

//     // =========================================================
//     // 6. Happy path — Main
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithMainDestination_ReturnsSuccess()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id, destination: PayoutDestination.Main);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("main", result.Data!.PayoutDestination);
//         Assert.Null(result.Data.UtilityType);
//         Assert.Null(result.Data.UtilityConfig);
//         Assert.Null(result.Data.UtilitySummary);
//     }

//     // =========================================================
//     // 7. Happy path — Utilities (all four types)
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithAirtimeUtility_HydratesUtilityConfig()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         var json = JsonSerializer.Serialize(new UtilityConfig
//         {
//             UtilityType = "airtime",
//             Network = "mtn",
//             PhoneNumber = "08012345678",
//         });

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             destination: PayoutDestination.Utilities,
//             utilityType: UtilityType.Airtime,
//             utilityConfigJson: json);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("utilities", result.Data!.PayoutDestination);
//         Assert.Equal("airtime", result.Data.UtilityType);
//         Assert.NotNull(result.Data.UtilityConfig);
//         Assert.Equal("airtime", result.Data.UtilityConfig!.UtilityType);
//         Assert.Equal("mtn", result.Data.UtilityConfig.Network);
//         Assert.Equal("08012345678", result.Data.UtilityConfig.PhoneNumber);
//         Assert.Contains("MTN", result.Data.UtilitySummary);
//         Assert.Contains("08012345678", result.Data.UtilitySummary);
//     }

//     [Fact]
//     public async Task Handle_WithDataUtility_HydratesUtilityConfig()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         var json = JsonSerializer.Serialize(new UtilityConfig
//         {
//             UtilityType = "data",
//             Network = "mtn",
//             PhoneNumber = "08012345678",
//             PlanCode = "mtn-2gb-7d",
//         });

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             destination: PayoutDestination.Utilities,
//             utilityType: UtilityType.Data,
//             utilityConfigJson: json);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("data", result.Data!.UtilityType);
//         Assert.Equal("mtn-2gb-7d", result.Data.UtilityConfig!.PlanCode);
//         Assert.Contains("mtn-2gb-7d", result.Data.UtilitySummary);
//     }

//     [Fact]
//     public async Task Handle_WithCableUtility_HydratesUtilityConfig()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         var json = JsonSerializer.Serialize(new UtilityConfig
//         {
//             UtilityType = "cable",
//             CableProvider = "DSTV",
//             SmartcardNumber = "1234567890",
//             PackageCode = "dstv-confam",
//         });

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             destination: PayoutDestination.Utilities,
//             utilityType: UtilityType.Cable,
//             utilityConfigJson: json);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("cable", result.Data!.UtilityType);
//         Assert.Equal("DSTV", result.Data.UtilityConfig!.CableProvider);
//         Assert.Equal("1234567890", result.Data.UtilityConfig.SmartcardNumber);
//         Assert.Contains("DSTV", result.Data.UtilitySummary);
//         Assert.Contains("7890", result.Data.UtilitySummary); // last 4 of smartcard
//     }

//     [Fact]
//     public async Task Handle_WithElectricityUtility_HydratesUtilityConfig()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         var json = JsonSerializer.Serialize(new UtilityConfig
//         {
//             UtilityType = "electricity",
//             Disco = "Ikeja Electric",
//             MeterNumber = "04512345678",
//             MeterType = "prepaid",
//         });

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             destination: PayoutDestination.Utilities,
//             utilityType: UtilityType.Electricity,
//             utilityConfigJson: json);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("electricity", result.Data!.UtilityType);
//         Assert.Equal("Ikeja Electric", result.Data.UtilityConfig!.Disco);
//         Assert.Equal("prepaid", result.Data.UtilityConfig.MeterType);
//         Assert.Contains("Ikeja Electric", result.Data.UtilitySummary);
//     }

//     // =========================================================
//     // 8. Utility edge cases
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithNullUtilityJson_ReturnsNullConfigButKeepsType()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             destination: PayoutDestination.Utilities,
//             utilityType: UtilityType.Airtime,
//             utilityConfigJson: null);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("airtime", result.Data!.UtilityType);
//         Assert.Null(result.Data.UtilityConfig);
//         Assert.Null(result.Data.UtilitySummary);
//     }

//     [Fact]
//     public async Task Handle_WithCorruptUtilityJson_DoesNotThrowAndReturnsNullConfig()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             destination: PayoutDestination.Utilities,
//             utilityType: UtilityType.Airtime,
//             utilityConfigJson: "{not valid json at all");

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("airtime", result.Data!.UtilityType);
//         Assert.Null(result.Data.UtilityConfig);
//     }

//     [Fact]
//     public async Task Handle_WithUtilityTypeNullOnUtilitiesWallet_ReturnsNullConfig()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         var json = JsonSerializer.Serialize(new UtilityConfig
//         {
//             UtilityType = "airtime",
//             Network = "mtn",
//             PhoneNumber = "08012345678",
//         });

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             destination: PayoutDestination.Utilities,
//             utilityType: null,
//             utilityConfigJson: json);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Null(result.Data!.UtilityType);
//         Assert.Null(result.Data.UtilityConfig);
//         Assert.Null(result.Data.UtilitySummary);
//     }

//     [Fact]
//     public async Task Handle_WithUtilityTypeSetButDestinationNotUtilities_ReturnsNullUtilityFields()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         var json = JsonSerializer.Serialize(new UtilityConfig
//         {
//             UtilityType = "airtime",
//             Network = "mtn",
//             PhoneNumber = "08012345678",
//         });

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             destination: PayoutDestination.Bank,
//             utilityType: UtilityType.Airtime,
//             utilityConfigJson: json);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("bank", result.Data!.PayoutDestination);
//         Assert.Null(result.Data.UtilityType);
//         Assert.Null(result.Data.UtilityConfig);
//         Assert.Null(result.Data.UtilitySummary);
//     }

//     [Fact]
//     public async Task Handle_WithEntityTypeOverridingJsonType_UsesEntityType()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         // JSON says "airtime" but entity says Cable — entity must win.
//         var json = JsonSerializer.Serialize(new UtilityConfig
//         {
//             UtilityType = "airtime",
//             Network = "mtn",
//             PhoneNumber = "08012345678",
//         });

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             destination: PayoutDestination.Utilities,
//             utilityType: UtilityType.Cable,
//             utilityConfigJson: json);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         Assert.Equal("cable", result.Data!.UtilityType);
//         Assert.Equal("cable", result.Data.UtilityConfig!.UtilityType);
//     }

//     // =========================================================
//     // 9. Release summary
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithNoReleases_ReturnsEmptySummary()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.IsSuccess);
//         var summary = result.Data!.ReleaseSummary;
//         Assert.Equal(0, summary.TotalReleases);
//         Assert.Equal(0, summary.ReleasedCount);
//         Assert.Equal(0, summary.ScheduledCount);
//         Assert.Equal(0, summary.FailedCount);
//         Assert.Equal(0m, summary.TotalReleasedAmount);
//         Assert.Equal(30_000m, summary.RemainingAmount);
//     }

//     [Fact]
//     public async Task Handle_WithReleases_ReturnsCorrectCounts()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);
//         var rule = await SeedRuleAsync(wallet.Id);

//         await SeedReleaseAsync(wallet.Id, rule.Id, 1_000m,
//             DateTimeOffset.UtcNow.AddDays(-2),
//             ReleaseStatus.Released,
//             DateTimeOffset.UtcNow.AddDays(-2));

//         await SeedReleaseAsync(wallet.Id, rule.Id, 1_000m,
//             DateTimeOffset.UtcNow.AddDays(-1),
//             ReleaseStatus.Released,
//             DateTimeOffset.UtcNow.AddDays(-1));

//         await SeedReleaseAsync(wallet.Id, rule.Id, 1_000m,
//             DateTimeOffset.UtcNow.AddDays(1),
//             ReleaseStatus.Scheduled);

//         await SeedReleaseAsync(wallet.Id, rule.Id, 1_000m,
//             DateTimeOffset.UtcNow.AddDays(2),
//             ReleaseStatus.Failed);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         var summary = result.Data!.ReleaseSummary;
//         Assert.Equal(4, summary.TotalReleases);
//         Assert.Equal(2, summary.ReleasedCount);
//         Assert.Equal(1, summary.ScheduledCount);
//         Assert.Equal(1, summary.FailedCount);
//     }

//     [Fact]
//     public async Task Handle_WithReleases_ComputesTotalAndAverage()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);
//         var rule = await SeedRuleAsync(wallet.Id);

//         await SeedReleaseAsync(wallet.Id, rule.Id, 1_000m,
//             DateTimeOffset.UtcNow.AddDays(-2), ReleaseStatus.Released,
//             DateTimeOffset.UtcNow.AddDays(-2));

//         await SeedReleaseAsync(wallet.Id, rule.Id, 2_000m,
//             DateTimeOffset.UtcNow.AddDays(-1), ReleaseStatus.Released,
//             DateTimeOffset.UtcNow.AddDays(-1));

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         var summary = result.Data!.ReleaseSummary;
//         Assert.Equal(3_000m, summary.TotalReleasedAmount);
//         Assert.Equal(1_500m, summary.AverageReleaseAmount);
//     }

//     [Fact]
//     public async Task Handle_WithProjectedReleases_CountsThemInUpcoming()
//     {
//         var previewReleases = new List<GetWalletSchedulePreviewQuery.ReleaseDto>
//         {
//             new() { ScheduledFor = DateTimeOffset.UtcNow.AddDays(1), Amount = 1_000m, IsProjected = true },
//             new() { ScheduledFor = DateTimeOffset.UtcNow.AddDays(2), Amount = 1_000m, IsProjected = true },
//         };

//         SetupUserExists();
//         SetupSchedulePreview(previewReleases);
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);
//         var rule = await SeedRuleAsync(wallet.Id);

//         await SeedReleaseAsync(wallet.Id, rule.Id, 1_000m,
//             DateTimeOffset.UtcNow.AddDays(3), ReleaseStatus.Scheduled);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         var summary = result.Data!.ReleaseSummary;
//         Assert.Equal(2, summary.ProjectedCount);
//         Assert.Equal(3, summary.UpcomingReleases); // 1 scheduled + 2 projected
//     }

//     // =========================================================
//     // 10. Progress
//     // =========================================================

//     [Fact]
//     public async Task Handle_Progress_IsCalculatedFromLockedAndTarget()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         // Target 30k, Locked 15k → 50% released
//         var wallet = await SeedWalletAsync(
//             category.Id,
//             targetNaira: 30_000m,
//             lockedNaira: 15_000m,
//             totalReleasedNaira: 15_000m);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Equal(50m, result.Data!.ProgressPercentage);
//     }

//     [Fact]
//     public async Task Handle_Progress_ClampedBetweenZeroAndHundred()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         // Locked > target → negative → clamp to 0
//         var wallet = await SeedWalletAsync(
//             category.Id,
//             targetNaira: 30_000m,
//             lockedNaira: 40_000m);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Equal(0m, result.Data!.ProgressPercentage);
//     }

//     [Fact]
//     public async Task Handle_Progress_ZeroWhenTargetIsZero()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id, targetNaira: 0m, lockedNaira: 0m);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Equal(0m, result.Data!.ProgressPercentage);
//     }

//     // =========================================================
//     // 11. Next release display
//     // =========================================================

//     [Fact]
//     public async Task Handle_NextReleaseDisplay_NoUpcoming_WhenNone()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Null(result.Data!.NextReleaseDate);
//         Assert.Equal("No upcoming releases", result.Data.NextReleaseDisplay);
//     }

//     [Fact]
//     public async Task Handle_NextReleaseDisplay_Tomorrow()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);
//         var rule = await SeedRuleAsync(wallet.Id);

//         await SeedReleaseAsync(wallet.Id, rule.Id, 1_000m,
//             DateTimeOffset.UtcNow.AddDays(1).AddHours(1));

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Equal("Tomorrow", result.Data!.NextReleaseDisplay);
//     }

//     [Fact]
//     public async Task Handle_NextReleaseDisplay_FallsBackToProjectedWhenNoScheduled()
//     {
//         var previewReleases = new List<GetWalletSchedulePreviewQuery.ReleaseDto>
//         {
//             new() { ScheduledFor = DateTimeOffset.UtcNow.AddDays(1).AddHours(2), Amount = 1_000m, IsProjected = true },
//         };

//         SetupUserExists();
//         SetupSchedulePreview(previewReleases);
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.NotNull(result.Data!.NextReleaseDate);
//         Assert.Equal("Tomorrow", result.Data.NextReleaseDisplay);
//     }

//     [Fact]
//     public async Task Handle_LastReleaseDisplay_NoReleasesYet_WhenNoneReleased()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Null(result.Data!.LastReleaseDate);
//         Assert.Equal("No releases yet", result.Data.LastReleaseDisplay);
//     }

//     [Fact]
//     public async Task Handle_LastReleaseDisplay_PicksMostRecentRelease()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);
//         var rule = await SeedRuleAsync(wallet.Id);

//         await SeedReleaseAsync(wallet.Id, rule.Id, 1_000m,
//             DateTimeOffset.UtcNow.AddDays(-5), ReleaseStatus.Released,
//             DateTimeOffset.UtcNow.AddDays(-5));

//         await SeedReleaseAsync(wallet.Id, rule.Id, 2_000m,
//             DateTimeOffset.UtcNow.AddDays(-1), ReleaseStatus.Released,
//             DateTimeOffset.UtcNow.AddDays(-1));

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.NotNull(result.Data!.LastReleaseDate);
//         Assert.True(result.Data.LastReleaseDate!.Value > DateTimeOffset.UtcNow.AddDays(-2));
//     }

//     // =========================================================
//     // 12. Automation / renewal policy
//     // =========================================================

//     [Fact]
//     public async Task Handle_WithNoRenewalPolicy_HasAutomationFalse()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.False(result.Data!.HasAutomation);
//         Assert.Null(result.Data.AutomationStatus);
//     }

//     [Fact]
//     public async Task Handle_WithActiveRenewalPolicy_HasAutomationTrue()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);
//         await SeedRenewalPolicyAsync(wallet.Id);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.True(result.Data!.HasAutomation);
//         Assert.Equal("Active", result.Data.AutomationStatus);
//     }

//     // =========================================================
//     // 13. Mediator interaction
//     // =========================================================

//     [Fact]
//     public async Task Handle_SendsSchedulePreviewQueryExactlyOnce()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);

//         var handler = CreateHandler();
//         await handler.Handle(CreateQuery(wallet.Id), default);

//         _mediator.Verify(
//             x => x.Send(
//                 It.Is<GetWalletSchedulePreviewQuery.Query>(q =>
//                     q.WalletId == wallet.Id &&
//                     q.UserPublicId == UserPublicId),
//                 It.IsAny<CancellationToken>()),
//             Times.Once);
//     }

//     // =========================================================
//     // 14. Error path
//     // =========================================================

//     [Fact]
//     public async Task Handle_WhenSchedulePreviewFails_StillReturnsWalletDetails()
//     {
//         SetupUserExists();
//         SetupSchedulePreview(isSuccess: false, errorMessage: "Preview failed");
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         // Handler does not fail on preview failure — only uses preview for
//         // projected counts / next-release fallback.
//         Assert.True(result.IsSuccess);
//         Assert.NotNull(result.Data);
//         Assert.Empty(result.Data!.SchedulePreview);
//     }

//     // =========================================================
//     // 15. Category + audit fields
//     // =========================================================

//     [Fact]
//     public async Task Handle_ReturnsCategoryNameAndIcon()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync("Rent & Housing", "Home");
//         var wallet = await SeedWalletAsync(category.Id);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Equal(category.Id, result.Data!.CategoryId);
//         Assert.Equal("Rent & Housing", result.Data.CategoryName);
//         Assert.Equal("Home", result.Data.CategoryIcon);
//     }

//     [Fact]
//     public async Task Handle_ReturnsRestartCount()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();
//         var wallet = await SeedWalletAsync(category.Id, restartCount: 3);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Equal(3, result.Data!.RestartCount);
//     }

//     [Fact]
//     public async Task Handle_ReturnsAllAmounts()
//     {
//         SetupUserExists();
//         SetupSchedulePreview();
//         var category = await SeedCategoryAsync();

//         var wallet = await SeedWalletAsync(
//             category.Id,
//             targetNaira: 50_000m,
//             lockedNaira: 20_000m,
//             availableNaira: 5_000m,
//             totalReleasedNaira: 30_000m,
//             totalWithdrawnNaira: 25_000m,
//             resetNaira: 10_000m);

//         var handler = CreateHandler();
//         var result = await handler.Handle(CreateQuery(wallet.Id), default);

//         Assert.Equal(50_000m, result.Data!.TargetAmount);
//         Assert.Equal(20_000m, result.Data.LockedAmount);
//         Assert.Equal(5_000m, result.Data.AvailableAmount);
//         Assert.Equal(30_000m, result.Data.TotalReleasedAmount);
//         Assert.Equal(25_000m, result.Data.TotalWithdrawnAmount);
//         Assert.Equal(10_000m, result.Data.ResetAmount);
//     }
// }