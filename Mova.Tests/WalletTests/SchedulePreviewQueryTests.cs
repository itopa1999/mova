using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Mova.Application.BBL.Queries.SchedulePreview;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Enums;
using Xunit;
using SchedulePreviewQuery = Mova.Application.BBL.Queries.SchedulePreview.SchedulePreviewQuery;

namespace Mova.Tests.Handlers;

public sealed class SchedulePreviewQueryTests : BaseTest
{
    private readonly Mock<ISchedulePreviewService> _previewService = new();

    private SchedulePreviewQuery.Handler CreateHandler()
    {
        return new SchedulePreviewQuery.Handler(
            _previewService.Object,
            NullLogger<SchedulePreviewQuery.Handler>.Instance);
    }

    private SchedulePreviewQuery.Query CreateQuery(
        decimal targetAmount = 1000m,
        decimal releaseAmount = 100m,
        ReleaseFrequency frequencyType = ReleaseFrequency.Monthly,
        string frequencyConfig = "{}",
        DateTimeOffset? startDate = null,
        int maxReleases = 50)
    {
        return new SchedulePreviewQuery.Query
        {
            TargetAmount = targetAmount,
            ReleaseAmount = releaseAmount,
            FrequencyType = frequencyType,
            FrequencyConfig = frequencyConfig,
            StartDate = startDate ?? DateTimeOffset.UtcNow,
            MaxReleases = maxReleases,
        };
    }

    private void SetupService(SchedulePreviewResult result)
    {
        _previewService
            .Setup(x => x.PreviewScheduleAsync(
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<ReleaseFrequency>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private void SetupServiceThrows()
    {
        _previewService
            .Setup(x => x.PreviewScheduleAsync(
                It.IsAny<decimal>(),
                It.IsAny<decimal>(),
                It.IsAny<ReleaseFrequency>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated failure"));
    }

    private static SchedulePreviewResult BuildValidResult()
    {
        var first = DateTimeOffset.UtcNow.AddDays(7);
        return new SchedulePreviewResult
        {
            IsSuccess = true,
            Description = "Weekly releases",
            TargetAmount = 1000m,
            ReleaseAmount = 100m,
            RegularReleaseAmount = 100m,
            FinalReleaseAmount = 100m,
            TotalReleases = 10,
            TotalAmount = 1000m,
            FirstReleaseDate = first,
            ComputedEndDate = first.AddDays(63),
            WeeksToReachTarget = 10,
            MonthsToReachTarget = 2,
            FrequencyType = ReleaseFrequency.Weekly,
            Errors = new List<string>(),
            Warnings = new List<string>(),
            SampleReleaseDates = new List<ReleaseDatePreview>
            {
                new() { Date = first, Amount = 100m, ReleaseNumber = 1, CumulativeAmount = 100m },
                new() { Date = first.AddDays(7), Amount = 100m, ReleaseNumber = 2, CumulativeAmount = 200m },
            },
        };
    }

    [Fact]
    public async Task Handle_WithValidPreview_ReturnsOkAndResponse()
    {
        SetupService(BuildValidResult());

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("Schedule preview generated successfully.", result.Message);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.IsValid);
    }

    [Fact]
    public async Task Handle_CallsServiceWithAllQueryFields()
    {
        SetupService(BuildValidResult());

        var start = DateTimeOffset.UtcNow.AddDays(3);
        var handler = CreateHandler();
        await handler.Handle(
            CreateQuery(
                targetAmount: 5000m,
                releaseAmount: 250m,
                frequencyType: ReleaseFrequency.Weekly,
                frequencyConfig: "{\"dayOfWeek\":1}",
                startDate: start,
                maxReleases: 30),
            default);

        _previewService.Verify(
            x => x.PreviewScheduleAsync(
                5000m,
                250m,
                ReleaseFrequency.Weekly,
                "{\"dayOfWeek\":1}",
                start,
                30,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_MapsAllResponseFields()
    {
        var preview = BuildValidResult();
        SetupService(preview);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        var dto = result.Data!;
        Assert.Equal(preview.Description, dto.Description);
        Assert.Equal(preview.TargetAmount, dto.TargetAmount);
        Assert.Equal(preview.ReleaseAmount, dto.ReleaseAmount);
        Assert.Equal(preview.RegularReleaseAmount, dto.RegularReleaseAmount);
        Assert.Equal(preview.FinalReleaseAmount, dto.FinalReleaseAmount);
        Assert.Equal(preview.TotalReleases, dto.TotalReleases);
        Assert.Equal(preview.TotalAmount, dto.TotalAmount);
        Assert.Equal(preview.FirstReleaseDate, dto.FirstReleaseDate);
        Assert.Equal(preview.ComputedEndDate, dto.ComputedEndDate);
        Assert.Equal(preview.WeeksToReachTarget, dto.WeeksToReachTarget);
        Assert.Equal(preview.MonthsToReachTarget, dto.MonthsToReachTarget);
        Assert.Equal(preview.FrequencyType, dto.FrequencyType);
    }

    [Fact]
    public async Task Handle_MapsSampleReleaseDates()
    {
        SetupService(BuildValidResult());

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Equal(2, result.Data!.SampleReleaseDates.Count);
        Assert.Equal(1, result.Data.SampleReleaseDates[0].ReleaseNumber);
        Assert.Equal(100m, result.Data.SampleReleaseDates[0].Amount);
        Assert.Equal(100m, result.Data.SampleReleaseDates[0].CumulativeAmount);
        Assert.Equal(2, result.Data.SampleReleaseDates[1].ReleaseNumber);
        Assert.Equal(200m, result.Data.SampleReleaseDates[1].CumulativeAmount);
    }

    [Fact]
    public async Task Handle_WhenPreviewHasErrors_ReturnsBadRequest()
    {
        var preview = BuildValidResult();
        preview.Errors = new List<string> { "Invalid frequency config" };
        SetupService(preview);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(
            "Schedule preview failed. Please check your configuration.",
            result.Message);
        Assert.NotNull(result.Data);
        Assert.False(result.Data!.IsValid);
        Assert.Single(result.Data.Errors);
    }

    [Fact]
    public async Task Handle_WhenPreviewNotSuccessful_ReturnsBadRequest()
    {
        var preview = BuildValidResult();
        preview.IsSuccess = false;
        SetupService(preview);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.False(result.Data!.IsValid);
    }

    [Fact]
    public async Task Handle_IsValid_FalseWhenErrorsEvenIfSuccess()
    {
        var preview = BuildValidResult();
        preview.Errors = new List<string> { "capacity exceeded" };
        SetupService(preview);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.Data!.IsValid);
    }

    [Fact]
    public async Task Handle_PassesThroughWarnings()
    {
        var preview = BuildValidResult();
        preview.Warnings = new List<string> { "Long schedule" };
        SetupService(preview);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Single(result.Data!.Warnings);
        Assert.Equal("Long schedule", result.Data.Warnings[0]);
    }

    [Fact]
    public async Task Handle_WhenServiceThrows_ReturnsInternalServerError()
    {
        SetupServiceThrows();

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        Assert.Equal(
            "Unable to generate the schedule preview. Please try again later.",
            result.Message);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Handle_WithEmptySampleDates_ReturnsEmptyList()
    {
        var preview = BuildValidResult();
        preview.SampleReleaseDates = new List<ReleaseDatePreview>();
        SetupService(preview);

        var handler = CreateHandler();
        var result = await handler.Handle(CreateQuery(), default);

        Assert.Empty(result.Data!.SampleReleaseDates);
    }
}