using System.Text.Json;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Services;

public sealed class SavingsCircleService : ISavingsCircleService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] DayLabels =
    {
        "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"
    };

    public Task<(bool Success, string Message)> ValidateFrequencyAsync(
        ContributionFrequency frequency,
        string frequencyConfig,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = ValidateInternal(frequency, frequencyConfig);
        return Task.FromResult(result);
    }

    private static (bool Success, string Message) ValidateInternal(
        ContributionFrequency frequency,
        string frequencyConfig)
    {
        if (!Enum.IsDefined(typeof(ContributionFrequency), frequency))
            return (false, $"Invalid frequency: {frequency}");

        FrequencyConfigDto? config;
        try
        {
            config = JsonSerializer.Deserialize<FrequencyConfigDto>(
                frequencyConfig, JsonOptions);
        }
        catch (JsonException)
        {
            return (false, "Frequency config is not valid JSON.");
        }

        if (config is null)
            return (false, "Frequency config is required.");

        var expectedType = frequency.ToString().ToLowerInvariant();
        var actualType = config.Type?.Trim().ToLowerInvariant();

        if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
            return (false,
                $"Frequency config type '{config.Type}' does not match frequency '{frequency}'.");

        switch (frequency)
        {
            case ContributionFrequency.Daily:
                if (config.DaysOfWeek is { Count: > 0 } daily
                    && daily.Any(d => d < 1 || d > 7))
                {
                    return (false, "daysOfWeek must contain values between 1 and 7.");
                }
                break;

            case ContributionFrequency.Weekly:
                if (config.DaysOfWeek is null || config.DaysOfWeek.Count == 0)
                    return (false, "Weekly frequency requires at least one day in daysOfWeek.");

                if (config.DaysOfWeek.Any(d => d < 1 || d > 7))
                    return (false, "daysOfWeek must contain values between 1 and 7.");
                break;

            case ContributionFrequency.Monthly:
                var hasDates = config.DatesOfMonth is { Count: > 0 };
                var hasLastDay = config.IsLastDayOfMonth;

                if (!hasDates && !hasLastDay)
                    return (false, "Monthly frequency requires datesOfMonth or isLastDayOfMonth.");

                if (hasDates && config.DatesOfMonth!.Any(d => d < 1 || d > 31))
                    return (false, "datesOfMonth must contain values between 1 and 31.");
                break;

            default:
                return (false, $"Unsupported frequency: {frequency}.");
        }

        return (true, string.Empty);
    }

    public string HumanizeFrequency(
        ContributionFrequency frequency,
        string frequencyConfig)
    {
        if (string.IsNullOrWhiteSpace(frequencyConfig))
            return frequency.ToString();

        FrequencyConfigDto? config;
        try
        {
            config = JsonSerializer.Deserialize<FrequencyConfigDto>(
                frequencyConfig, JsonOptions);
        }
        catch (JsonException)
        {
            return frequency.ToString();
        }

        if (config is null)
            return frequency.ToString();

        return frequency switch
        {
            ContributionFrequency.Daily => HumanizeDaily(config),
            ContributionFrequency.Weekly => HumanizeWeekly(config),
            ContributionFrequency.Monthly => HumanizeMonthly(config),
            _ => frequency.ToString()
        };
    }

    private static string HumanizeDaily(FrequencyConfigDto config)
    {
        if (config.DaysOfWeek is { Count: 7 })
            return "every day";

        if (config.DaysOfWeek is { Count: > 0 })
            return $"every day on {FormatDays(config.DaysOfWeek)}";

        return "every day";
    }

    private static string HumanizeWeekly(FrequencyConfigDto config)
    {
        if (config.DaysOfWeek is { Count: > 0 })
            return $"weekly on {FormatDays(config.DaysOfWeek)}";

        return "weekly";
    }

    private static string HumanizeMonthly(FrequencyConfigDto config)
    {
        var dates = (config.DatesOfMonth ?? new List<int>())
            .Where(d => d is >= 1 and <= 31)
            .OrderBy(d => d)
            .Select(FormatOrdinal)
            .ToList();

        if (dates.Count > 0 && config.IsLastDayOfMonth)
            return $"monthly on {string.Join(", ", dates)}, and the last day of the month";

        if (dates.Count > 0)
            return $"monthly on {string.Join(", ", dates)}";

        if (config.IsLastDayOfMonth)
            return "monthly on the last day of the month";

        return "monthly";
    }

    private static string FormatDays(IEnumerable<int> daysOfWeek)
    {
        return string.Join(", ", daysOfWeek
            .Where(d => d is >= 1 and <= 7)
            .OrderBy(d => d)
            .Select(d => DayLabels[d - 1]));
    }

    private static string FormatOrdinal(int day)
    {
        var suffix = (day % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (day % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            }
        };
        return $"the {day}{suffix}";
    }

    private sealed class FrequencyConfigDto
    {
        public string? Type { get; set; }
        public List<int>? DaysOfWeek { get; set; }
        public List<int>? DatesOfMonth { get; set; }
        public bool IsLastDayOfMonth { get; set; }
    }
}