using Mova.Domain.Enums;

namespace Mova.Application.Interfaces.Service;

/// <summary>
/// Service for previewing release schedules without creating or saving anything
/// </summary>
public interface ISchedulePreviewService
{
    /// <summary>
    /// Generates a preview of the release schedule based on the provided configuration
    /// </summary>
    /// <param name="targetAmount">Total amount to save/reach</param>
    /// <param name="releaseAmount">Amount per release</param>
    /// <param name="frequencyType">Frequency type (Once, Daily, Weekly, etc.)</param>
    /// <param name="frequencyConfig">JSON configuration for the frequency</param>
    /// <param name="startDate">Start date of the schedule</param>
    /// <param name="maxReleases">Maximum number of releases to show (default 50)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Schedule preview with release dates and summary</returns>
    Task<SchedulePreviewResult> PreviewScheduleAsync(
        decimal targetAmount,
        decimal releaseAmount,
        ReleaseFrequency frequencyType,
        string frequencyConfig,
        DateTimeOffset startDate,
        int maxReleases = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of schedule preview
/// </summary>
public class SchedulePreviewResult
{
    public bool IsSuccess { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal ReleaseAmount { get; set; }
    public decimal RegularReleaseAmount { get; set; }
    public decimal FinalReleaseAmount { get; set; }
    public int TotalReleases { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset FirstReleaseDate { get; set; }
    public DateTimeOffset ComputedEndDate { get; set; }
    public int WeeksToReachTarget { get; set; }
    public int MonthsToReachTarget { get; set; }
    public ReleaseFrequency FrequencyType { get; set; }
    public List<ReleaseDatePreview> SampleReleaseDates { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Individual release date preview
/// </summary>
public class ReleaseDatePreview
{
    public DateTimeOffset Date { get; set; }
    public decimal Amount { get; set; }
    public int ReleaseNumber { get; set; }
    public decimal CumulativeAmount { get; set; }
}