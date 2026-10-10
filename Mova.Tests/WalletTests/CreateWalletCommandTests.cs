using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class CreateWalletCommandTests : BaseTest
{
    private const string UserPublicId = "user_create_test";
    private const string UserEmail = "user@mova.app";
    private const string UserFirstName = "Lucky";

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<ISchedulePreviewService> _schedulePreview = new();
    private readonly Mock<IWalletRuleService> _walletRuleService = new();
    private readonly Mock<INotificationQueue> _notifications = new();

    private CreateWalletCommand.Handler CreateHandler()
    {
        return new CreateWalletCommand.Handler(
            _identityService.Object,
            UnitOfWork,
            Mock.Of<ILogger<CreateWalletCommand.Handler>>(),
            _schedulePreview.Object,
            _walletRuleService.Object,
            _notifications.Object);
    }

    private CreateWalletCommand.Command CreateCommand(
        long categoryId,
        long bankAccountId,
        string name = "Rent Savings",
        string payoutDestination = "bank")
    {
        return new CreateWalletCommand.Command
        {
            UserPublicId = UserPublicId,
            Email = UserEmail,
            FirstName = UserFirstName,
            Name = name,
            Description = "Monthly rent",
            CategoryId = categoryId,
            BankAccountId = bankAccountId,
            TargetAmount = 30_000m,
            Frequency = ReleaseFrequency.Daily,
            FrequencyConfig = "{}",
            AmountToBeReleased = 1_000m,
            StartDate = DateTimeOffset.UtcNow.Date.AddDays(1),
            PayoutDestination = payoutDestination,
        };
    }

    private async Task<(WalletCategory category, BankAccount bank)> SeedPrerequisitesAsync()
    {
        var category = new WalletCategory
        {
            Name = "Rent & Housing",
            Icon = "Wallet",
        };

        var bank = new BankAccount
        {
            UserPublicId = UserPublicId,
            AccountNumber = "0123456789",
            AccountName = "Lucky Starboy",
            BankCode = "058",
            BankName = "GTBank",
            BankImageUrl = "https://example.com/gtb.png",
            Status = BankAccountStatus.Active,
            IsDefault = true,
            ConsentGiven = true,
            ConsentGivenAt = DateTimeOffset.UtcNow,
            ConsentVersion = "v1",
            Currency = "NGN",
            VerifiedAt = DateTimeOffset.UtcNow,
            VerificationMessage = "Verified",
        };

        await UnitOfWork.AddAsync(category);
        await UnitOfWork.AddAsync(bank);
        await UnitOfWork.SaveChangesAsync();

        return (category, bank);
    }

    private void SetupHappyPathDependencies(int totalReleases = 30)
    {
        _identityService
            .Setup(x => x.DebitBalanceAsync(
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _schedulePreview
            .Setup(x => x.PreviewScheduleAsync(
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<ReleaseFrequency>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulePreviewResult
            {
                IsSuccess = true,
                TotalReleases = totalReleases,
            });

        _walletRuleService
            .Setup(x => x.GetNextReleaseAsync(
                It.IsAny<WalletRule>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletRule rule, DateTimeOffset after, CancellationToken _) =>
                new NextWalletRelease
                {
                    ScheduledFor = after.AddDays(1),
                    Amount = rule.Amount,
                });
    }

    private static UtilityConfig BuildAirtimeConfig()
        => new()
        {
            UtilityType = "airtime",
            Network = "mtn",
            PhoneNumber = "08012345678",
        };

    private static UtilityConfig BuildDataConfig()
        => new()
        {
            UtilityType = "data",
            Network = "mtn",
            PhoneNumber = "08012345678",
            PlanCode = "mtn-2gb-7d",
        };

    private static UtilityConfig BuildCableConfig()
        => new()
        {
            UtilityType = "cable",
            CableProvider = "DSTV",
            SmartcardNumber = "1234567890",
            PackageCode = "dstv-confam",
        };

    private static UtilityConfig BuildElectricityConfig()
        => new()
        {
            UtilityType = "electricity",
            Disco = "Ikeja Electric",
            MeterNumber = "04512345678",
            MeterType = "prepaid",
        };

    // =========================================================
    // 1. Happy path — Bank
    // =========================================================

    [Fact]
    public async Task Handle_WithValidRequest_CreatesWallet()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.WalletId > 0);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == result.Data.WalletId);

        Assert.NotNull(wallet);
        Assert.Equal("Rent Savings", wallet!.Name);
        Assert.Equal(WalletStatus.Active, wallet.Status);
        Assert.Equal(30_000m, wallet.TargetAmount.ToDecimal());
        Assert.Equal(30_000m, wallet.LockedAmount.ToDecimal());
        Assert.Equal(0m, wallet.AvailableAmount.ToDecimal());
        Assert.Equal(PayoutDestination.Bank, wallet.PayoutDestination);
    }

    [Fact]
    public async Task Handle_WithValidRequest_DebitsBalanceExactlyOnce()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(category.Id, bank.Id), default);

        _identityService.Verify(
            x => x.DebitBalanceAsync(
                UserPublicId,
                30_695m,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CreatesWalletCreatedTransaction()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var walletId = result.Data!.WalletId;

        var tx = await Context.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Reference == $"wallet-created:{walletId}");

        Assert.NotNull(tx);
        Assert.Equal(TransactionType.Deposit, tx!.Type);
        Assert.Equal(TransactionStatus.Completed, tx.Status);
        Assert.Equal(30_000m, tx.Amount.ToDecimal());
    }

    [Fact]
    public async Task Handle_WithValidRequest_CreatesWalletRule()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var rule = await Context.WalletRules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WalletId == result.Data!.WalletId);

        Assert.NotNull(rule);
        Assert.Equal(1_000m, rule!.Amount.ToDecimal());
        Assert.Equal(ReleaseFrequency.Daily, rule.Frequency);
    }

    [Fact]
    public async Task Handle_WithValidRequest_SchedulesFirstRelease()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var release = await Context.ScheduledReleases
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WalletId == result.Data!.WalletId);

        Assert.NotNull(release);
        Assert.Equal(ReleaseStatus.Scheduled, release!.Status);
        Assert.True(release.ScheduledFor > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Handle_WithValidRequest_CommitsTransactionExactlyOnce()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(category.Id, bank.Id), default);

        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.CommitCount);
        Assert.Equal(0, UnitOfWork.RollbackCount);
    }

    // =========================================================
    // 2. Happy path — Wallet destination
    // =========================================================

[Fact]
public async Task Handle_WithWalletDestination_CreatesWalletWithWalletDestination()
{
    var (category, _) = await SeedPrerequisitesAsync();
    SetupHappyPathDependencies();

    var handler = CreateHandler();
    var result = await handler.Handle(
        CreateCommand(category.Id, 0, payoutDestination: "wallet"), default);

    Assert.True(result.IsSuccess);

    var wallet = await Context.Wallets
        .AsNoTracking()
        .FirstAsync(x => x.Id == result.Data!.WalletId);

    Assert.Equal(PayoutDestination.Wallet, wallet.PayoutDestination);
    Assert.Null(wallet.BankAccountId);
    Assert.Null(wallet.UtilityType);
    Assert.Null(wallet.UtilityConfigJson);
}
    [Fact]
    public async Task Handle_WithWalletDestinationAndBankAccountProvided_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id, payoutDestination: "wallet"), default);

        // bank.Id > 0 → should be rejected
        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithWalletDestinationAndZeroBankAccount_Succeeds()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, 0, payoutDestination: "wallet"), default);

        Assert.True(result.IsSuccess);
    }

    // =========================================================
    // 3. Happy path — Main destination
    // =========================================================

    [Fact]
    public async Task Handle_WithMainDestination_CreatesWalletWithMainDestination()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, 0, payoutDestination: "main"), default);

        Assert.True(result.IsSuccess);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == result.Data!.WalletId);

        Assert.Equal(PayoutDestination.Main, wallet.PayoutDestination);
        Assert.Null(wallet.UtilityType);
        Assert.Null(wallet.UtilityConfigJson);
    }

    // =========================================================
    // 4. Happy path — Utilities (all four types)
    // =========================================================

    [Fact]
    public async Task Handle_WithAirtimeUtility_CreatesWalletWithUtilityConfig()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = BuildAirtimeConfig();

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == result.Data!.WalletId);

        Assert.Equal(PayoutDestination.Utilities, wallet.PayoutDestination);
        Assert.Equal(UtilityType.Airtime, wallet.UtilityType);
        Assert.NotNull(wallet.UtilityConfigJson);

        var parsed = JsonSerializer.Deserialize<UtilityConfig>(wallet.UtilityConfigJson!);
        Assert.NotNull(parsed);
        Assert.Equal("airtime", parsed!.UtilityType);
        Assert.Equal("mtn", parsed.Network);
        Assert.Equal("08012345678", parsed.PhoneNumber);
    }

    [Fact]
    public async Task Handle_WithDataUtility_CreatesWalletWithUtilityConfig()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = BuildDataConfig();

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == result.Data!.WalletId);

        Assert.Equal(UtilityType.Data, wallet.UtilityType);

        var parsed = JsonSerializer.Deserialize<UtilityConfig>(wallet.UtilityConfigJson!);
        Assert.NotNull(parsed);
        Assert.Equal("data", parsed!.UtilityType);
        Assert.Equal("mtn-2gb-7d", parsed.PlanCode);
    }

    [Fact]
    public async Task Handle_WithCableUtility_CreatesWalletWithUtilityConfig()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = BuildCableConfig();

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == result.Data!.WalletId);

        Assert.Equal(UtilityType.Cable, wallet.UtilityType);

        var parsed = JsonSerializer.Deserialize<UtilityConfig>(wallet.UtilityConfigJson!);
        Assert.NotNull(parsed);
        Assert.Equal("cable", parsed!.UtilityType);
        Assert.Equal("DSTV", parsed.CableProvider);
        Assert.Equal("1234567890", parsed.SmartcardNumber);
        Assert.Equal("dstv-confam", parsed.PackageCode);
    }

    [Fact]
    public async Task Handle_WithElectricityUtility_CreatesWalletWithUtilityConfig()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = BuildElectricityConfig();

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == result.Data!.WalletId);

        Assert.Equal(UtilityType.Electricity, wallet.UtilityType);

        var parsed = JsonSerializer.Deserialize<UtilityConfig>(wallet.UtilityConfigJson!);
        Assert.NotNull(parsed);
        Assert.Equal("electricity", parsed!.UtilityType);
        Assert.Equal("Ikeja Electric", parsed.Disco);
        Assert.Equal("04512345678", parsed.MeterNumber);
        Assert.Equal("prepaid", parsed.MeterType);
    }

    // =========================================================
    // 5. Utilities validation failures
    // =========================================================

    [Fact]
    public async Task Handle_WithUtilitiesDestinationAndNoConfig_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, 0, payoutDestination: "utilities"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WithUnknownUtilityType_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig { UtilityType = "water" };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithAirtimeButMissingNetwork_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "airtime",
            PhoneNumber = "08012345678",
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithAirtimeInvalidNetwork_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "airtime",
            Network = "vodacom",
            PhoneNumber = "08012345678",
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithAirtimeInvalidPhone_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "airtime",
            Network = "mtn",
            PhoneNumber = "12345", // too short
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithDataButMissingPlanCode_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "data",
            Network = "mtn",
            PhoneNumber = "08012345678",
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithCableButMissingProvider_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "cable",
            SmartcardNumber = "1234567890",
            PackageCode = "dstv-confam",
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithCableInvalidProvider_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "cable",
            CableProvider = "SkyTV",
            SmartcardNumber = "1234567890",
            PackageCode = "dstv-confam",
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithCableButMissingPackageCode_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "cable",
            CableProvider = "DSTV",
            SmartcardNumber = "1234567890",
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithElectricityInvalidMeterType_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "electricity",
            Disco = "Ikeja Electric",
            MeterNumber = "04512345678",
            MeterType = "smart",
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithElectricityMissingMeterNumber_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "electricity",
            Disco = "Ikeja Electric",
            MeterType = "prepaid",
        };

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithBankDestinationAndUtilityConfig_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, bank.Id, payoutDestination: "bank");
        command.UtilityConfig = BuildAirtimeConfig();

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithWalletDestinationAndUtilityConfig_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "wallet");
        command.UtilityConfig = BuildAirtimeConfig();

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithMainDestinationAndUtilityConfig_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "main");
        command.UtilityConfig = BuildAirtimeConfig();

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    // =========================================================
    // 6. Validation failures — general
    // =========================================================

    [Fact]
    public async Task Handle_WithEmptyName_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.Name = string.Empty;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, UnitOfWork.BeginCount);
        Assert.Equal(0, UnitOfWork.CommitCount);
    }

    [Fact]
    public async Task Handle_WithNameTooLong_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.Name = new string('a', 151);

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithTargetBelowMinimum_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.TargetAmount = 1_000m;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithReleaseAmountBelowMinimum_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.AmountToBeReleased = 50m;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithReleaseGreaterThanTarget_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var command = CreateCommand(category.Id, bank.Id);
        command.AmountToBeReleased = 40_000m;

        var handler = CreateHandler();
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithDuplicateName_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        var existing = new Wallet
        {
            UserPublicId = UserPublicId,
            Name = "Rent Savings",
            CategoryId = category.Id,
            BankAccountId = bank.Id,
            TargetAmount = Money.FromNaira(30_000m),
            FundedAmount = Money.FromNaira(30_000m),
            LockedAmount = Money.FromNaira(30_000m),
            AvailableAmount = Money.FromNaira(0),
            TotalReleasedAmount = Money.FromNaira(0),
            Status = WalletStatus.Active,
        };
        await UnitOfWork.AddAsync(existing);
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id, name: "Rent Savings"), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithUnknownCategory_ReturnsBadRequest()
    {
        var (_, bank) = await SeedPrerequisitesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: 999_999L, bankAccountId: bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithUnknownBankAccount_ReturnsBadRequest()
    {
        var (category, _) = await SeedPrerequisitesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(categoryId: category.Id, bankAccountId: 999_999L), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithBankAccountLackingConsent_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        bank.ConsentGiven = false;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithInactiveBankAccount_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        bank.Status = BankAccountStatus.Suspended;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task Handle_WithInsufficientBalance_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        _identityService
            .Setup(x => x.DebitBalanceAsync(
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, UnitOfWork.BeginCount);
        Assert.Equal(1, UnitOfWork.RollbackCount);
    }

    [Fact]
    public async Task Handle_WithInvalidSchedulePreview_ReturnsBadRequest()
    {
        var (category, bank) = await SeedPrerequisitesAsync();

        _schedulePreview
            .Setup(x => x.PreviewScheduleAsync(
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<ReleaseFrequency>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulePreviewResult
            {
                IsSuccess = false,
                Errors = new List<string> { "Invalid schedule" },
            });

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    // =========================================================
    // 7. REGRESSION: notifications must never fail the request
    // =========================================================

    [Fact]
    public async Task Handle_WhenInAppNotificationThrows_StillReturnsSuccess()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Redis down"));

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.True(result.IsSuccess);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == result.Data!.WalletId);

        Assert.NotNull(wallet);
    }

    [Fact]
    public async Task Handle_WhenEmailQueueThrows_StillReturnsSuccess()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new InvalidOperationException("Hangfire unavailable"));

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WhenBothNotificationsThrow_StillReturnsSuccess()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Exception("boom"));

        _notifications
            .Setup(x => x.QueueNotificationEmail(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Throws(new Exception("boom"));

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_WhenInAppThrows_EmailIsStillAttempted()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        _notifications
            .Setup(x => x.InAppNotificationAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new Exception("boom"));

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(category.Id, bank.Id), default);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    // =========================================================
    // 8. REGRESSION: notification content
    // =========================================================

    [Fact]
    public async Task Handle_WithValidRequest_SendsNotificationWithCorrectContent()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(category.Id, bank.Id), default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                "Rent Savings wallet created",
                It.Is<string>(m => m.Contains("Rent Savings")),
                "/wallets",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _notifications.Verify(
            x => x.QueueNotificationEmail(
                UserFirstName,
                UserEmail,
                It.Is<string>(m => m.Contains("Rent Savings")),
                "Your Rent Savings wallet is ready"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithAirtimeUtility_MentionsAirtimeInNotification()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = BuildAirtimeConfig();

        var handler = CreateHandler();
        await handler.Handle(command, default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                It.IsAny<string>(),
                It.Is<string>(m =>
                    m.Contains("MTN") &&
                    m.Contains("08012345678")),
                "/wallets",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithCableUtility_MentionsCableProviderInNotification()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = BuildCableConfig();

        var handler = CreateHandler();
        await handler.Handle(command, default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                It.IsAny<string>(),
                It.Is<string>(m => m.Contains("DSTV")),
                "/wallets",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithElectricityUtility_MentionsDiscoInNotification()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = BuildElectricityConfig();

        var handler = CreateHandler();
        await handler.Handle(command, default);

        _notifications.Verify(
            x => x.InAppNotificationAsync(
                UserPublicId,
                NotificationType.Wallet,
                It.IsAny<string>(),
                It.Is<string>(m => m.Contains("Ikeja Electric")),
                "/wallets",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================
    // 9. REGRESSION: no side effects on failure
    // =========================================================

    [Fact]
    public async Task Handle_WhenUtilityValidationFails_NoWalletPersisted()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var command = CreateCommand(category.Id, 0, payoutDestination: "utilities");
        command.UtilityConfig = new UtilityConfig
        {
            UtilityType = "airtime",
            Network = "mtn",
            // missing phone number
        };

        var handler = CreateHandler();
        await handler.Handle(command, default);

        var walletCount = await Context.Wallets.CountAsync();
        Assert.Equal(0, walletCount);
        Assert.Equal(0, UnitOfWork.BeginCount);
    }

    [Fact]
    public async Task Handle_WhenBankValidationFails_NoBalanceDebit()
    {
        var (category, _) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(category.Id, bankAccountId: 999_999L), default);

        _identityService.Verify(
            x => x.DebitBalanceAsync(
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenInsufficientBalance_NoWalletPersisted()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        _identityService
            .Setup(x => x.DebitBalanceAsync(
                It.IsAny<string>(),
                It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = CreateHandler();
        await handler.Handle(CreateCommand(category.Id, bank.Id), default);

        var walletCount = await Context.Wallets.CountAsync();
        Assert.Equal(0, walletCount);
    }

    // =========================================================
    // 10. REGRESSION: ledger invariants
    // =========================================================

    [Fact]
    public async Task Handle_WithValidRequest_CreatesMatchingLedgerEntry()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var ledger = await Context.LedgerEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WalletId == result.Data!.WalletId);

        Assert.NotNull(ledger);
        Assert.True(ledger!.IsCredit);
        Assert.Equal(30_000m, ledger.Amount.ToDecimal());
    }

    [Fact]
    public async Task Handle_WithValidRequest_LedgerReferencesMatchingTransaction()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var walletId = result.Data!.WalletId;

        var ledger = await Context.LedgerEntries
            .AsNoTracking()
            .FirstAsync(x => x.WalletId == walletId);

        var tx = await Context.Transactions
            .AsNoTracking()
            .FirstAsync(x => x.Id == ledger.TransactionId);

        Assert.Equal(tx.Id, ledger.TransactionId);
        Assert.Equal(tx.Amount.ToDecimal(), ledger.Amount.ToDecimal());
    }

    [Fact]
    public async Task Handle_WithValidRequest_CreatesSeparateFeeTransaction()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var feeTx = await Context.Transactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Reference == $"wallet-fee:{result.Data!.WalletId}");

        Assert.NotNull(feeTx);
        Assert.Equal(TransactionType.Fee, feeTx!.Type);
    }

    // =========================================================
    // 11. REGRESSION: wallet balance initial state
    // =========================================================

    [Fact]
    public async Task Handle_WithValidRequest_LocksExactTargetAmount()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        var wallet = await Context.Wallets
            .AsNoTracking()
            .FirstAsync(x => x.Id == result.Data!.WalletId);

        Assert.Equal(wallet.TargetAmount.ToDecimal(), wallet.LockedAmount.ToDecimal());
        Assert.Equal(wallet.TargetAmount.ToDecimal(), wallet.FundedAmount.ToDecimal());
        Assert.Equal(0m, wallet.AvailableAmount.ToDecimal());
        Assert.Equal(0m, wallet.TotalReleasedAmount.ToDecimal());
        Assert.Equal(0m, wallet.TotalWithdrawnAmount.ToDecimal());
        Assert.Equal(0m, wallet.ResetAmount.ToDecimal());
    }

    [Fact]
    public async Task Handle_WithValidRequest_ReturnsNewMainBalanceFromIdentityService()
    {
        var (category, bank) = await SeedPrerequisitesAsync();
        SetupHappyPathDependencies();

        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                UserPublicId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                1,
                UserPublicId,
                UserFirstName,
                null,
                "Starboy",
                UserEmail,
                "08050000000",
                null,
                Money.FromNaira(69_305m),
                string.Empty,
                true,
                true,
                true,
                false,
                "Lucky Starboy",
                DateTimeOffset.UtcNow));

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(category.Id, bank.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(69_305m, result.Data!.NewMainBalance);
    }
}