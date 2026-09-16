using System.Net;
using Microsoft.EntityFrameworkCore;
using Mova.Application.BBL.Commands.FeatureFlags;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Xunit;

namespace Mova.Tests.Handlers;

public sealed class ToggleFeatureFlagTests : BaseTest
{
    private ToggleFeatureFlag.Handler CreateHandler()
    {
        return new ToggleFeatureFlag.Handler(UnitOfWork);
    }

    private ToggleFeatureFlag.Command CreateCommand(
        long id,
        bool isEnabled)
    {
        return new ToggleFeatureFlag.Command
        {
            Id = id,
            IsEnabled = isEnabled,
        };
    }

    // ---------------------------------------------------------
    // Seed helper
    // ---------------------------------------------------------

    private async Task<FeatureFlag> SeedFlagAsync(
        FeatureFlagName name = FeatureFlagName.AllowWithdrawFunds,
        bool isEnabled = false)
    {
        var flag = new FeatureFlag
        {
            Name = name,
            IsEnabled = isEnabled,
        };

        await UnitOfWork.AddAsync(flag);
        await UnitOfWork.SaveChangesAsync();
        return flag;
    }

    // =========================================================
    // 1. Not found
    // =========================================================

    [Fact]
    public async Task Handle_WithUnknownId_ReturnsNotFound()
    {
        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(id: 999_999, isEnabled: true),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Contains("not found", result.Message);
        Assert.Contains("999999", result.Message);
    }

    [Fact]
    public async Task Handle_WithUnknownId_DoesNotSaveChanges()
    {
        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(id: 999_999, isEnabled: true),
            default);

        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    // =========================================================
    // 2. Already in desired state (idempotent)
    // =========================================================

    [Fact]
    public async Task Handle_WhenAlreadyEnabled_ReturnsOkAndDoesNotSave()
    {
        var flag = await SeedFlagAsync(isEnabled: true);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(flag.Id, isEnabled: true),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("already enabled", result.Message);
        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WhenAlreadyDisabled_ReturnsOkAndDoesNotSave()
    {
        var flag = await SeedFlagAsync(isEnabled: false);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(flag.Id, isEnabled: false),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("already disabled", result.Message);
        Assert.Equal(0, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_WhenAlreadyEnabled_ReturnsCurrentState()
    {
        var flag = await SeedFlagAsync(
            name: FeatureFlagName.AllowWithdrawFunds,
            isEnabled: true);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(flag.Id, isEnabled: true),
            default);

        Assert.NotNull(result.Data);
        Assert.Equal(flag.Id, result.Data!.Id);
        Assert.Equal(FeatureFlagName.AllowWithdrawFunds, result.Data.Name);
        Assert.True(result.Data.IsEnabled);
    }

    // =========================================================
    // 3. Happy path — enable
    // =========================================================

    [Fact]
    public async Task Handle_EnablingDisabledFlag_ReturnsSuccess()
    {
        var flag = await SeedFlagAsync(isEnabled: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(flag.Id, isEnabled: true),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("enabled successfully", result.Message);
    }

    [Fact]
    public async Task Handle_EnablingDisabledFlag_PersistsChange()
    {
        var flag = await SeedFlagAsync(isEnabled: false);

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(flag.Id, isEnabled: true),
            default);

        var reloaded = await Context.FeatureFlags
            .AsNoTracking()
            .FirstAsync(x => x.Id == flag.Id);

        Assert.True(reloaded.IsEnabled);
    }

    [Fact]
    public async Task Handle_EnablingDisabledFlag_SavesChangesOnce()
    {
        var flag = await SeedFlagAsync(isEnabled: false);

        UnitOfWork.ResetCounts();

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(flag.Id, isEnabled: true),
            default);

        Assert.Equal(1, UnitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Handle_EnablingDisabledFlag_ReturnsUpdatedDto()
    {
        var flag = await SeedFlagAsync(
            name: FeatureFlagName.AllowWithdrawFunds,
            isEnabled: false);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(flag.Id, isEnabled: true),
            default);

        Assert.NotNull(result.Data);
        Assert.Equal(flag.Id, result.Data!.Id);
        Assert.Equal(FeatureFlagName.AllowWithdrawFunds, result.Data.Name);
        Assert.True(result.Data.IsEnabled);
    }

    // =========================================================
    // 4. Happy path — disable
    // =========================================================

    [Fact]
    public async Task Handle_DisablingEnabledFlag_ReturnsSuccess()
    {
        var flag = await SeedFlagAsync(isEnabled: true);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(flag.Id, isEnabled: false),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains("disabled successfully", result.Message);
    }

    [Fact]
    public async Task Handle_DisablingEnabledFlag_PersistsChange()
    {
        var flag = await SeedFlagAsync(isEnabled: true);

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(flag.Id, isEnabled: false),
            default);

        var reloaded = await Context.FeatureFlags
            .AsNoTracking()
            .FirstAsync(x => x.Id == flag.Id);

        Assert.False(reloaded.IsEnabled);
    }

    [Fact]
    public async Task Handle_DisablingEnabledFlag_ReturnsUpdatedDto()
    {
        var flag = await SeedFlagAsync(isEnabled: true);

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(flag.Id, isEnabled: false),
            default);

        Assert.NotNull(result.Data);
        Assert.False(result.Data!.IsEnabled);
    }

    // =========================================================
    // 5. Only the targeted flag is touched
    // =========================================================

    [Fact]
    public async Task Handle_OnlyTogglesTheTargetedFlag()
    {
        var target = await SeedFlagAsync(
            name: FeatureFlagName.AllowWithdrawFunds,
            isEnabled: false);
        var other = await SeedFlagAsync(
            name: FeatureFlagName.PayoutsViaFlutterwave,
            isEnabled: false);

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(target.Id, isEnabled: true),
            default);

        var reloadedTarget = await Context.FeatureFlags
            .AsNoTracking()
            .FirstAsync(x => x.Id == target.Id);
        var reloadedOther = await Context.FeatureFlags
            .AsNoTracking()
            .FirstAsync(x => x.Id == other.Id);

        Assert.True(reloadedTarget.IsEnabled);
        Assert.False(reloadedOther.IsEnabled);
    }

    // =========================================================
    // 6. ModifiedAt is populated by ApplicationDbContext
    // =========================================================

    [Fact]
    public async Task Handle_WhenToggling_ModifiedAtIsSet()
    {
        var flag = await SeedFlagAsync(isEnabled: false);
        var before = DateTimeOffset.UtcNow;

        var handler = CreateHandler();
        await handler.Handle(
            CreateCommand(flag.Id, isEnabled: true),
            default);

        var after = DateTimeOffset.UtcNow;

        var reloaded = await Context.FeatureFlags
            .AsNoTracking()
            .FirstAsync(x => x.Id == flag.Id);

        Assert.NotNull(reloaded.ModifiedAt);
        Assert.True(reloaded.ModifiedAt >= before);
        Assert.True(reloaded.ModifiedAt <= after);
    }

    // =========================================================
    // 7. Repeat toggles
    // =========================================================

    [Fact]
    public async Task Handle_WhenToggledBackAndForth_PersistsEachChange()
    {
        var flag = await SeedFlagAsync(isEnabled: false);

        var handler = CreateHandler();

        await handler.Handle(CreateCommand(flag.Id, isEnabled: true), default);
        await handler.Handle(CreateCommand(flag.Id, isEnabled: false), default);
        await handler.Handle(CreateCommand(flag.Id, isEnabled: true), default);

        var reloaded = await Context.FeatureFlags
            .AsNoTracking()
            .FirstAsync(x => x.Id == flag.Id);

        Assert.True(reloaded.IsEnabled);
    }

    // =========================================================
    // 8. Soft-deleted flag
    // =========================================================

    [Fact]
    public async Task Handle_WithSoftDeletedFlag_ReturnsNotFound()
    {
        var flag = await SeedFlagAsync(isEnabled: false);

        flag.IsDeleted = true;
        await UnitOfWork.SaveChangesAsync();

        var handler = CreateHandler();
        var result = await handler.Handle(
            CreateCommand(flag.Id, isEnabled: true),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
    }
}