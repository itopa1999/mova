using System.Net;
using System.Text.Json;
using Moq;
using MediatR;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.Interfaces.Identity;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Shared.Common;
using Xunit;
using WalletDetailsQuery = Mova.Application.BBL.Queries.AccountWallet.WalletDetails;

namespace Mova.Tests.Handlers;

public sealed class WalletDetailsQueryTests : BaseTest
{
    private const string UserPublicId = "user_wallet_details";
    private const string OtherUserPublicId = "someone_else";
    private const long UserId = 42;

    private readonly Mock<IIdentityService> _identityService = new();
    private readonly Mock<IMediator> _mediator = new();

    private WalletDetailsQuery.Handler CreateHandler()
    {
        return new WalletDetailsQuery.Handler(
            UnitOfWork,
            _identityService.Object,
            _mediator.Object);
    }

    private WalletDetailsQuery.Query CreateQuery(
        long walletId = 1,
        string? userPublicId = null)
    {
        return new WalletDetailsQuery.Query
        {
            UserPublicId = userPublicId ?? UserPublicId,
            WalletId = walletId,
        };
    }

    private void SetupUserExists(string publicId = UserPublicId)
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserIdentityDto(
                UserId, publicId, "Lucky", null, "Starboy",
                "user@mova.app", "08050000000", null,
                Money.FromNaira(0), string.Empty,
                true, true, true, false,
                string.Empty, DateTimeOffset.UtcNow));
    }

    private void SetupUserNotFound()
    {
        _identityService
            .Setup(x => x.GetByIdentifierAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserIdentityDto?)null);
    }

    private void SetupSchedulePreview(
        params (long? Id, DateTimeOffset ScheduledFor, decimal Amount,
                string Status, bool IsReleased, bool IsProjected,
                DateTimeOffset? ReleasedAt)[] items)
    {
        var releases = items.Select(i =>
            new GetWalletSchedulePreviewQuery.ReleaseDto
            {
                ScheduledReleaseId = i.Id,
                ScheduledFor = i.ScheduledFor,
                Amount = i.Amount,
                Status = i.Status,
                IsReleased = i.IsReleased,
                IsProjected = i.IsProjected,
                ReleasedAt = i.ReleasedAt,
            }).ToList();

        var dto = new GetWalletSchedulePreviewQuery.GetWalletSchedulePreviewResponseDto
        {
            Releases = releases,
        };

        _mediator
            .Setup(m => m.Send(
                It.IsAny<GetWalletSchedulePreviewQuery.Query>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new BaseResult<GetWalletSchedulePreviewQuery.GetWalletSchedulePreviewResponseDto>(
                    HttpStatusCode.OK, "ok", dto));
    }

    private void SetupSchedulePreviewFails()
    {
        _mediator
            .Setup(m => m.Send(
                It.IsAny<GetWalletSchedulePreviewQuery.Query>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new BaseResult<GetWalletSchedulePreviewQuery.GetWalletSchedulePreviewResponseDto>(
                    HttpStatusCode.InternalServerError, "preview failed"));
    }

    private async Task<WalletCategory> SeedCategoryAsync(
        string name = "Savings", string icon = "PiggyBank")
    {
        var category = new WalletCategory { Name = name, Icon = icon };
        await UnitOfWork.AddAsync(category);
        await UnitOfWork.SaveChangesAsync();
        return category;
    }

    private async Task<Wallet> SeedWalletAsync(
        string userPublicId = UserPublicId,
        string name = "Rent",
        long? categoryId = null,
        string? description = null,
        decimal targetNaira = 1000m,
        decimal lockedNaira = 400m,
        decimal releasedNaira = 300m,
        decimal availableNaira = 100m,
        decimal withdrawnNaira = 50m,
        decimal resetNaira = 0m,
        int restartCount = 0,
        PayoutDestination destination = PayoutDestination.Bank,
        UtilityType? utilityType = null,
        string? utilityConfigJson = null)
    {
        if (categoryId is null || categoryId <= 0)
            throw new InvalidOperationException("Category required.");

        var wallet = new Wallet
        {
            UserPublicId = userPublicId,
            Name = name,
            Description = description,
            CategoryId = categoryId.Value,
            TargetAmount = Money.FromNaira(targetNaira),
            LockedAmount = Money.FromNaira(lockedNaira),
            TotalReleasedAmount = Money.FromNaira(releasedNaira),
            AvailableAmount = Money.FromNaira(availableNaira),
            TotalWithdrawnAmount = Money.FromNaira(withdrawnNaira),
            FundedAmount = Money.FromNaira(0),
            ResetAmount = Money.FromNaira(resetNaira),
            Status = WalletStatus.Active,
            PayoutDestination = destination,
            RestartCount = restartCount,
            UtilityType = utilityType,
            UtilityConfigJson = utilityConfigJson,
        };

        await UnitOfWork.AddAsync(wallet);
        await UnitOfWork.SaveChangesAsync();
        return wallet;
    }

    private async Task<WalletRule> SeedWalletRuleAsync(
        long walletId,
        decimal amountNaira = 50m,
        ReleaseFrequency frequency = ReleaseFrequency.Monthly,
        string frequencyConfig = "{\"dayOfMonth\":1}")
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var rule = new WalletRule
        {
            WalletId = walletId,
            Amount = Money.FromNaira(amountNaira),
            Frequency = frequency,
            FrequencyConfig = frequencyConfig,
            StartDate = start,
            EndDate = start.AddYears(1),
        };
        await UnitOfWork.AddAsync(rule);
        await UnitOfWork.SaveChangesAsync();
        return rule;
    }

    private async Task<ScheduledRelease> SeedReleaseAsync(
        long walletId, long walletRuleId, decimal amountNaira,
        DateTimeOffset scheduledFor, DateTimeOffset? releasedAt,
        ReleaseStatus status)
    {
        var release = new ScheduledRelease
        {
            WalletId = walletId,
            WalletRuleId = walletRuleId,
            Amount = Money.FromNaira(amountNaira),
            ScheduledFor = scheduledFor,
            ReleasedAt = releasedAt,
            Status = status,
            FailedAttempts = 0,
        };
        await UnitOfWork.AddAsync(release);
        await UnitOfWork.SaveChangesAsync();
        return release;
    }

    private async Task<RenewalPolicy> SeedRenewalPolicyAsync(
        long walletId,
        RenewalStatus status = RenewalStatus.Active,
        RenewalTriggerType triggerType = RenewalTriggerType.OnCompletion,
        RefillAmountType refillAmountType = RefillAmountType.Fixed,
        decimal refillNaira = 500m)
    {
        var policy = new RenewalPolicy
        {
            WalletId = walletId,
            UserPublicId = UserPublicId,
            Status = status,
            TriggerType = triggerType,
            RefillAmountType = refillAmountType,
            RefillAmount = Money.FromNaira(refillNaira),
        };
        await UnitOfWork.AddAsync(policy);
        await UnitOfWork.SaveChangesAsync();
        return policy;
    }

    [Fact]
    public async Task Handle_WithEmptyUserPublicId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(userPublicId: ""), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("User public ID is required.", result.Message);
    }

    [Fact]
    public async Task Handle_WithNonPositiveWalletId_ReturnsBadRequest()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: 0), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid wallet ID.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ReturnsNotFound()
    {
        SetupUserNotFound();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("User not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownWallet_ReturnsNotFound()
    {
        SetupUserExists();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: 999_999), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Wallet not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithWalletOwnedByOtherUser_ReturnsNotFound()
    {
        SetupUserExists();
        var cat = await SeedCategoryAsync();
        var otherWallet = await SeedWalletAsync(OtherUserPublicId, "Theirs", cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: otherWallet.Id), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Wallet not found.", result.Message);
    }

    [Fact]
    public async Task Handle_WithWalletFound_ReturnsIdentityAndCategory()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync("Rent", "Home");
        var wallet = await SeedWalletAsync(
            name: "Rent Fund", categoryId: cat.Id, description: "Monthly rent");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(wallet.Id, result.Data!.WalletId);
        Assert.Equal("Rent Fund", result.Data.Name);
        Assert.Equal("Monthly rent", result.Data.Description);
        Assert.Equal(cat.Id, result.Data.CategoryId);
        Assert.Equal("Rent", result.Data.CategoryName);
        Assert.Equal("Home", result.Data.CategoryIcon);
    }

    [Fact]
    public async Task Handle_MapsAllAmountFields()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id,
            targetNaira: 1000m, lockedNaira: 400m, releasedNaira: 300m,
            availableNaira: 100m, withdrawnNaira: 50m, resetNaira: 10m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(1000m, result.Data!.TargetAmount);
        Assert.Equal(400m, result.Data.LockedAmount);
        Assert.Equal(300m, result.Data.TotalReleasedAmount);
        Assert.Equal(100m, result.Data.AvailableAmount);
        Assert.Equal(50m, result.Data.TotalWithdrawnAmount);
        Assert.Equal(10m, result.Data.ResetAmount);
    }

    [Fact]
    public async Task Handle_WithNoRule_ReportsNotConfigured()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(0m, result.Data!.ReleaseAmount);
        Assert.Equal("NotConfigured", result.Data.Frequency);
        Assert.Equal(string.Empty, result.Data.FrequencyConfig);
        Assert.Null(result.Data.EndDate);
    }

    [Fact]
    public async Task Handle_WithRule_MapsFrequencyAndConfig()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(
            wallet.Id, amountNaira: 75m,
            frequency: ReleaseFrequency.Weekly,
            frequencyConfig: "{\"dayOfWeek\":1}");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(75m, result.Data!.ReleaseAmount);
        Assert.Equal(nameof(ReleaseFrequency.Weekly), result.Data.Frequency);
        Assert.Equal("{\"dayOfWeek\":1}", result.Data.FrequencyConfig);
        Assert.Equal(rule.StartDate, result.Data.StartDate);
        Assert.NotNull(result.Data.EndDate);
    }

    [Fact]
    public async Task Handle_WithCompletedAndFailedReleases_PopulatesSummary()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id, targetNaira: 1000m);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m,
            DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(-5),
            ReleaseStatus.Released);

        await SeedReleaseAsync(wallet.Id, rule.Id, 200m,
            DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(-3),
            ReleaseStatus.Released);

        await SeedReleaseAsync(wallet.Id, rule.Id, 150m,
            DateTimeOffset.UtcNow.AddDays(1), null,
            ReleaseStatus.Scheduled);

        await SeedReleaseAsync(wallet.Id, rule.Id, 50m,
            DateTimeOffset.UtcNow.AddDays(-2), null,
            ReleaseStatus.Failed);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        var summary = result.Data!.ReleaseSummary;
        Assert.Equal(4, summary.TotalReleases);
        Assert.Equal(2, summary.ReleasedCount);
        Assert.Equal(1, summary.ScheduledCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(300m, summary.TotalReleasedAmount);
        Assert.Equal(150m, summary.AverageReleaseAmount);
        Assert.Equal(700m, summary.RemainingAmount);
    }

    [Fact]
public async Task Handle_NextReleaseDate_PicksEarliestFutureScheduled()
{
    SetupUserExists();
    SetupSchedulePreview();

    var cat = await SeedCategoryAsync();
    var wallet = await SeedWalletAsync(categoryId: cat.Id);
    var rule = await SeedWalletRuleAsync(wallet.Id);

    var soon = DateTimeOffset.UtcNow.AddDays(2).AddHours(1);
    var later = DateTimeOffset.UtcNow.AddDays(10);

    await SeedReleaseAsync(wallet.Id, rule.Id, 100m, later, null,
        ReleaseStatus.Scheduled);
    await SeedReleaseAsync(wallet.Id, rule.Id, 100m, soon, null,
        ReleaseStatus.Scheduled);

    var handler = CreateHandler();
    var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

    Assert.Equal(soon, result.Data!.NextReleaseDate);
    Assert.Equal("2 days", result.Data.NextReleaseDisplay);
}

    [Fact]
    public async Task Handle_NextReleaseDate_FallsBackToProjectedWhenNoScheduledExists()
    {
        SetupUserExists();

        var projected = DateTimeOffset.UtcNow.AddDays(5);
        SetupSchedulePreview(
            (null, projected, 50m, nameof(ReleaseStatus.Scheduled),
                false, true, null));

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedWalletRuleAsync(wallet.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(projected, result.Data!.NextReleaseDate);
    }

    [Fact]
    public async Task Handle_LastReleaseDate_PicksMostRecentCompleted()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        var rule = await SeedWalletRuleAsync(wallet.Id);

        var older = DateTimeOffset.UtcNow.AddDays(-10);
        var newer = DateTimeOffset.UtcNow.AddDays(-2);

        await SeedReleaseAsync(wallet.Id, rule.Id, 100m, older, older,
            ReleaseStatus.Released);
        await SeedReleaseAsync(wallet.Id, rule.Id, 100m, newer, newer,
            ReleaseStatus.Released);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(newer, result.Data!.LastReleaseDate);
    }

    [Fact]
    public async Task Handle_ProgressPercentage_WithNoPolicy_UsesTargetAmount()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id, targetNaira: 1000m, lockedNaira: 400m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(60m, result.Data!.ProgressPercentage);
    }

    [Fact]
    public async Task Handle_ProgressPercentage_WithActiveOnCompletionPolicy_UsesRefillAmountForNonFixed()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id, targetNaira: 1000m, lockedNaira: 200m);
        await SeedRenewalPolicyAsync(
            wallet.Id,
            status: RenewalStatus.Active,
            triggerType: RenewalTriggerType.OnCompletion,
            refillAmountType: RefillAmountType.Custom,
            refillNaira: 500m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(60m, result.Data!.ProgressPercentage);
    }

    [Fact]
    public async Task Handle_ProgressPercentage_ClampedTo100()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id, targetNaira: 100m, lockedNaira: 0m);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(100m, result.Data!.ProgressPercentage);
    }

    [Fact]
    public async Task Handle_WithRenewalPolicy_SetsHasAutomation()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);
        await SeedRenewalPolicyAsync(wallet.Id, status: RenewalStatus.Paused);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.True(result.Data!.HasAutomation);
        Assert.Equal(nameof(RenewalStatus.Paused), result.Data.AutomationStatus);
    }

    [Fact]
    public async Task Handle_WithNoRenewalPolicy_HasAutomationIsFalse()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.False(result.Data!.HasAutomation);
        Assert.Null(result.Data.AutomationStatus);
    }

    [Fact]
    public async Task Handle_WithBankDestination_DoesNotHydrateUtilityFields()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id, destination: PayoutDestination.Bank);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal("bank", result.Data!.PayoutDestination);
        Assert.Null(result.Data.UtilityType);
        Assert.Null(result.Data.UtilityConfig);
        Assert.Null(result.Data.UtilitySummary);
    }

    [Fact]
    public async Task Handle_WithUtilitiesAirtimeDestination_HydratesUtilityConfigAndSummary()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var json = JsonSerializer.Serialize(new
        {
            UtilityType = "airtime",
            Network = "mtn",
            PhoneNumber = "08031234567",
        });

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id,
            destination: PayoutDestination.Utilities,
            utilityType: UtilityType.Airtime,
            utilityConfigJson: json);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal("utilities", result.Data!.PayoutDestination);
        Assert.Equal("airtime", result.Data.UtilityType);
        Assert.NotNull(result.Data.UtilityConfig);
        Assert.Equal("mtn", result.Data.UtilityConfig!.Network);
        Assert.Equal("08031234567", result.Data.UtilityConfig.PhoneNumber);
        Assert.Equal(
            "MTN airtime to 08031234567",
            result.Data.UtilitySummary);
    }

    [Fact]
    public async Task Handle_WithUtilitiesElectricityDestination_MasksMeterInSummary()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var json = JsonSerializer.Serialize(new
        {
            UtilityType = "electricity",
            Disco = "IKEDC",
            MeterNumber = "1234567890",
            MeterType = "Prepaid",
        });

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id,
            destination: PayoutDestination.Utilities,
            utilityType: UtilityType.Electricity,
            utilityConfigJson: json);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal("IKEDC · ••••7890 (Prepaid)", result.Data!.UtilitySummary);
    }

    [Fact]
    public async Task Handle_WithMalformedUtilityJson_ReturnsWalletWithoutUtilityConfig()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id,
            destination: PayoutDestination.Utilities,
            utilityType: UtilityType.Airtime,
            utilityConfigJson: "{ not-valid-json");

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data!.UtilityConfig);
        Assert.Null(result.Data.UtilitySummary);
    }

    [Fact]
    public async Task Handle_WhenSchedulePreviewFails_StillReturnsWalletDetails()
    {
        SetupUserExists();
        SetupSchedulePreviewFails();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!.SchedulePreview);
        Assert.Equal(0, result.Data.ReleaseSummary.ProjectedCount);
    }

    [Fact]
    public async Task Handle_ProjectsSchedulePreviewItems()
    {
        SetupUserExists();

        var projected = DateTimeOffset.UtcNow.AddDays(10);
        var released = DateTimeOffset.UtcNow.AddDays(-5);

        SetupSchedulePreview(
            (1, released, 100m, nameof(ReleaseStatus.Released),
                true, false, released),
            (null, projected, 50m, nameof(ReleaseStatus.Scheduled),
                false, true, null));

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(categoryId: cat.Id);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(2, result.Data!.SchedulePreview.Count);

        var releasedItem = result.Data.SchedulePreview[0];
        Assert.Equal(1, releasedItem.ScheduledReleaseId);
        Assert.True(releasedItem.IsReleased);
        Assert.False(releasedItem.IsProjected);
        Assert.NotEqual("Not released", releasedItem.ReleasedAtDisplay);

        var projectedItem = result.Data.SchedulePreview[1];
        Assert.Null(projectedItem.ScheduledReleaseId);
        Assert.False(projectedItem.IsReleased);
        Assert.True(projectedItem.IsProjected);
        Assert.Equal("Not released", projectedItem.ReleasedAtDisplay);
    }

    [Fact]
    public async Task Handle_MapsLifecycleAndAuditFields()
    {
        SetupUserExists();
        SetupSchedulePreview();

        var cat = await SeedCategoryAsync();
        var wallet = await SeedWalletAsync(
            categoryId: cat.Id, restartCount: 3);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(walletId: wallet.Id), default);

        Assert.Equal(3, result.Data!.RestartCount);
        Assert.NotEqual(default, result.Data.CreatedAt);
    }
}