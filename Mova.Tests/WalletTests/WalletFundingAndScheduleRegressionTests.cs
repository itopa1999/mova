using System.Data;
using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Mova.Application.BBL.Commands.AccountWallet;
using Mova.Application.BBL.Queries.AccountWallet;
using Mova.Application.Interfaces.Identity;
using Mova.Application.Interfaces.Notification;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Security;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;
using Mova.Infrastructure.Persistence;
using Mova.Infrastructure.Service;

namespace Mova.Tests.WalletTests;

#if false
public sealed class WalletFundingAndScheduleRegressionTests
{
    [Fact]
    public async Task CreateWallet_daily_schedule_creates_first_release_and_calculates_final_remainder_date()
    {
        await using var context = CreateContext();
        var unitOfWork = new RecordingUnitOfWork(context);
        var identity = new BalanceIdentityService(50_000m);
        var handler = CreateWalletHandler(unitOfWork, identity, SuccessfulPreview(5));

        var result = await handler.Handle(ValidCreateRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.Equal(20_000m, identity.Balance);
        Assert.Equal(1, unitOfWork.BeginCount);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Equal(0, unitOfWork.RollbackCount);

        var wallet = await context.Wallets.SingleAsync();
        var rule = await context.WalletRules.SingleAsync();
        var release = await context.ScheduledReleases.SingleAsync();
        Assert.Equal(30_000m, wallet.TargetAmount.ToDecimal());
        Assert.Equal(30_000m, wallet.LockedAmount.ToDecimal());
        Assert.Equal(7_000m, release.Amount.ToDecimal());
        Assert.Equal(ReleaseStatus.Scheduled, release.Status);
        Assert.Equal(new DateTimeOffset(2030, 9, 7, 9, 30, 0, TimeSpan.Zero), release.ScheduledFor);
        Assert.Equal(new DateTimeOffset(2030, 9, 11, 9, 30, 0, TimeSpan.Zero), rule.EndDate);
    }

    [Fact]
    public async Task Schedule_preview_includes_all_future_releases_including_final_2000_remainder()
    {
        await using var context = CreateContext();
        var unitOfWork = new RecordingUnitOfWork(context);
        var identity = new BalanceIdentityService(50_000m);
        var create = CreateWalletHandler(unitOfWork, identity, SuccessfulPreview(5));
        var created = await create.Handle(ValidCreateRequest(), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var handler = new GetWalletSchedulePreviewQuery.Handler(unitOfWork, new WalletRuleService());
        var result = await handler.Handle(new GetWalletSchedulePreviewQuery.Query
        {
            WalletId = created.Data!.WalletId,
            UserPublicId = "user-1"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { 7_000m, 7_000m, 7_000m, 7_000m, 2_000m }, result.Data!.Releases.Select(x => x.Amount));
        Assert.Single(result.Data.Releases.Where(x => !x.IsProjected));
        var finalRelease = result.Data.Releases.Last();
        Assert.True(finalRelease.IsProjected);
        Assert.False(finalRelease.IsReleased);
        Assert.Equal(2_000m, finalRelease.Amount);
        Assert.Equal(new DateTimeOffset(2030, 9, 11, 10, 30, 0, TimeSpan.FromHours(1)), finalRelease.ScheduledFor);
    }

    [Theory]
    [InlineData(0, 7_000)]
    [InlineData(30_000, 0)]
    [InlineData(30_000, 30_001)]
    public async Task CreateWallet_invalid_amount_does_not_start_or_mutate_a_transaction(decimal target, decimal releaseAmount)
    {
        await using var context = CreateContext();
        var unitOfWork = new RecordingUnitOfWork(context);
        var identity = new BalanceIdentityService(50_000m);
        var request = ValidCreateRequest();
        request.TargetAmount = target;
        request.AmountToBeReleased = releaseAmount;

        var result = await CreateWalletHandler(unitOfWork, identity, SuccessfulPreview(5))
            .Handle(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(50_000m, identity.Balance);
        Assert.Equal(0, unitOfWork.BeginCount);
        Assert.Empty(context.Wallets);
        Assert.Empty(context.WalletRules);
        Assert.Empty(context.ScheduledReleases);
    }

    [Fact]
    public async Task CreateWallet_insufficient_balance_rolls_back_without_creating_records()
    {
        await using var context = CreateContext();
        var unitOfWork = new RecordingUnitOfWork(context);
        var identity = new BalanceIdentityService(29_999m);

        var result = await CreateWalletHandler(unitOfWork, identity, SuccessfulPreview(5))
            .Handle(ValidCreateRequest(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, unitOfWork.BeginCount);
        Assert.Equal(1, unitOfWork.RollbackCount);
        Assert.Equal(0, unitOfWork.CommitCount);
        Assert.Empty(context.Wallets);
    }

    [Fact]
    public async Task CreateWallet_invalid_schedule_returns_validation_error_without_debiting_user()
    {
        await using var context = CreateContext();
        var unitOfWork = new RecordingUnitOfWork(context);
        var identity = new BalanceIdentityService(50_000m);
        var preview = new SchedulePreviewResult { IsSuccess = false, Errors = ["Invalid daily schedule"] };

        var result = await CreateWalletHandler(unitOfWork, identity, preview)
            .Handle(ValidCreateRequest(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Contains("Invalid daily schedule", result.Message);
        Assert.Equal(50_000m, identity.Balance);
        Assert.Equal(0, unitOfWork.BeginCount);
    }

    private static CreateWalletCommand.Handler CreateWalletHandler(
        IUnitOfWork unitOfWork,
        IIdentityService identity,
        SchedulePreviewResult preview)
    {
        var pinChallenge = new Mock<IPinChallenge>();
        pinChallenge
            .Setup(x => x.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<PinChallengeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, string.Empty));

        return new CreateWalletCommand.Handler(
            identity,
            unitOfWork,
            NullLogger<CreateWalletCommand.Handler>.Instance,
            new FixedPreviewService(preview),
            new WalletRuleService(),
            new Mock<INotificationQueue>().Object,
            pinChallenge.Object);
    }

    private static CreateWalletCommand.Command ValidCreateRequest() => new()
    {
        UserPublicId = "user-1",
        Name = "Daily Spending",
        TargetAmount = 30_000m,
        AmountToBeReleased = 7_000m,
        Frequency = ReleaseFrequency.Daily,
        FrequencyConfig = "{\"type\":\"daily\",\"time\":\"10:30\",\"daysOfWeek\":[1,2,3,4,5,6,7]}",
        StartDate = new DateTimeOffset(2030, 9, 6, 10, 30, 0, TimeSpan.FromHours(1))
    };

    private static SchedulePreviewResult SuccessfulPreview(int totalReleases) => new()
    {
        IsSuccess = true,
        TotalReleases = totalReleases
    };

    private static ApplicationDbContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new ApplicationDbContext(options, null);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class FixedPreviewService(SchedulePreviewResult result) : ISchedulePreviewService
    {
        public Task<SchedulePreviewResult> PreviewScheduleAsync(decimal targetAmount, decimal releaseAmount, ReleaseFrequency frequencyType, string frequencyConfig, DateTimeOffset startDate, int maxReleases = 50, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class BalanceIdentityService(decimal balance, bool userExists = true) : IIdentityService
    {
        public decimal Balance { get; private set; } = balance;

        public Task<(bool Success, string ErrorMessage, string UserPublicId, long UserId)> CreateUserAsync(
            string firstName,
            string lastName,
            string email,
            string phoneNumber,
            string bvn,
            string password,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<(bool Success, string ErrorMessage)> AddToRoleAsync(
            long userId,
            string role,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<(bool Success, string ErrorMessage)> RemoveFromRoleAsync(
            long userId,
            string role,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<UserIdentityDto?> GetByIdentifierAsync(
            string identifier,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<bool> EmailExistsAsync(
            string email,
            long? excludeUserId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> BvnExistsAsync(
            string bvnHash,
            long? excludeUserId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> PhoneExistsAsync(
            string phoneNumber,
            long? excludeUserId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<(bool Success, string ErrorMessage)> MarkEmailAndPhoneAsVerifiedAsync(
            long userId,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(
            long userId,
            string newPassword,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<(bool Success, string ErrorMessage)> ChangePasswordAsync(
            long userId,
            string oldPassword,
            string newPassword,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<bool> UpdateNotificationPreferenceAsync(
            string identifier,
            string key,
            bool enabled,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> UpdateLastKnownDeviceAsync(
            string identifier,
            string deviceId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> CheckPasswordAsync(
            long userId,
            string password,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> IsAccountVerifiedAsync(
            long userId,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IList<string>> GetRolesAsync(
            long userId,
            CancellationToken cancellationToken = default) => Task.FromResult<IList<string>>([]);

        public Task<bool> CreditBalanceAsync(string userPublicId, decimal amount, CancellationToken cancellationToken)
        {
            if (!userExists || amount <= 0) return Task.FromResult(false);
            Balance += amount;
            return Task.FromResult(true);
        }

        public Task<bool> DebitBalanceAsync(string userPublicId, decimal amount, CancellationToken cancellationToken) => Task.FromResult(userExists && Balance >= amount && Debit(amount));

        public Task<int> CountUsersInRoleAsync(string role, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<bool> IsLockedOutAsync(
            long userId,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<DateTimeOffset?> GetLockoutEndAsync(
            long userId,
            CancellationToken cancellationToken = default) => Task.FromResult<DateTimeOffset?>(null);

        public Task RecordFailedAccessAsync(
            long userId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResetFailedAccessAsync(
            long userId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<UserPermissionsDto?> GetPermissionsAsync(
            string userPublicId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<UserPermissionsDto?>(new UserPermissionsDto(
                userPublicId,
                UserAccountStatus.Active,
                "Active",
                "Active",
                true,
                true,
                true,
                true,
                true,
                null,
                null,
                null,
                null));

        public Task<bool> CloseAccountAsync(
            string userPublicId,
            AccountRestrictionReason reason,
            string details,
            string performedBy,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        private bool Debit(decimal amount) { Balance -= amount; return true; }
    }

    private sealed class RecordingUnitOfWork(ApplicationDbContext context) : IUnitOfWork
    {
        public int BeginCount { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public IQueryable<T> Query<T>() where T : class => context.Set<T>();
        public Task AddAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class => context.Set<T>().AddAsync(entity, cancellationToken).AsTask();
        public void Update<T>(T entity) where T : class => context.Set<T>().Update(entity);
        public void Remove<T>(T entity) where T : class => context.Set<T>().Remove(entity);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) { BeginCount++; return Task.CompletedTask; }
        public Task BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default) { BeginCount++; return Task.CompletedTask; }
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) { CommitCount++; return Task.CompletedTask; }
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) { RollbackCount++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#endif
