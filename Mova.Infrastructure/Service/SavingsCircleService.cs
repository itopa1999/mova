using System.Text.Json;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Enums;
namespace Mova.Infrastructure.Service;

public sealed class SavingsCircleService : ISavingsCircleService
{
    private const int DefaultHourOfDay = 9;

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

    public DateTimeOffset GetNextCollectionDate(
        ContributionFrequency frequency,
        string frequencyConfig,
        DateTimeOffset from)
    {
        if (!Enum.IsDefined(typeof(ContributionFrequency), frequency))
            throw new InvalidOperationException(
                $"Invalid frequency: {frequency}.");

        if (string.IsNullOrWhiteSpace(frequencyConfig))
            throw new InvalidOperationException(
                "Frequency config is required.");

        FrequencyConfigDto? config;
        try
        {
            config = JsonSerializer.Deserialize<FrequencyConfigDto>(
                frequencyConfig, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Frequency config is not valid JSON.", ex);
        }

        if (config is null)
            throw new InvalidOperationException(
                "Frequency config is required.");

        var expectedType = frequency.ToString().ToLowerInvariant();
        var actualType = config.Type?.Trim().ToLowerInvariant();

        if (!string.Equals(actualType, expectedType, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Frequency config type '{config.Type}' does not match " +
                $"frequency '{frequency}'.");

        return frequency switch
        {
            ContributionFrequency.Daily => GetNextDailyDate(config, from),
            ContributionFrequency.Weekly => GetNextWeeklyDate(config, from),
            ContributionFrequency.Monthly => GetNextMonthlyDate(config, from),
            _ => throw new InvalidOperationException(
                $"Unsupported frequency: {frequency}.")
        };
    }

    private static DateTimeOffset GetNextDailyDate(
        FrequencyConfigDto config,
        DateTimeOffset from)
    {
        var daysOfWeek = SanitizeDaysOfWeek(config.DaysOfWeek);

        if (daysOfWeek.Count == 0 || daysOfWeek.Count == 7)
        {
            var todayAtHour = new DateTimeOffset(
                from.Year, from.Month, from.Day,
                DefaultHourOfDay, 0, 0, from.Offset);

            return todayAtHour > from
                ? todayAtHour
                : todayAtHour.AddDays(1);
        }

        return GetNextMatchingDayOfWeek(daysOfWeek, from);
    }

    private static DateTimeOffset GetNextWeeklyDate(
        FrequencyConfigDto config,
        DateTimeOffset from)
    {
        var daysOfWeek = SanitizeDaysOfWeek(config.DaysOfWeek);

        if (daysOfWeek.Count == 0)
            throw new InvalidOperationException(
                "Weekly frequency requires at least one day in daysOfWeek.");

        return GetNextMatchingDayOfWeek(daysOfWeek, from);
    }

    private static DateTimeOffset GetNextMonthlyDate(
        FrequencyConfigDto config,
        DateTimeOffset from)
    {
        var datesOfMonth = SanitizeDatesOfMonth(config.DatesOfMonth);
        var isLastDay = config.IsLastDayOfMonth;

        if (datesOfMonth.Count == 0 && !isLastDay)
            throw new InvalidOperationException(
                "Monthly frequency requires datesOfMonth or isLastDayOfMonth.");

        var thisMonthCandidates = BuildMonthlyCandidates(
            from.Year, from.Month, datesOfMonth, isLastDay, from.Offset);

        var nextThisMonth = thisMonthCandidates
            .Where(c => c > from)
            .OrderBy(c => c)
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();

        if (nextThisMonth.HasValue)
            return nextThisMonth.Value;

        var next = from.AddMonths(1);
        var nextMonthCandidates = BuildMonthlyCandidates(
            next.Year, next.Month, datesOfMonth, isLastDay, from.Offset);

        return nextMonthCandidates.Min();
    }

    private static List<DateTimeOffset> BuildMonthlyCandidates(
        int year,
        int month,
        List<int> datesOfMonth,
        bool isLastDay,
        TimeSpan offset)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var result = new List<DateTimeOffset>(datesOfMonth.Count + 1);

        foreach (var day in datesOfMonth)
        {
            var validDay = Math.Min(day, daysInMonth);
            result.Add(new DateTimeOffset(
                year, month, validDay,
                DefaultHourOfDay, 0, 0, offset));
        }

        if (isLastDay)
        {
            result.Add(new DateTimeOffset(
                year, month, daysInMonth,
                DefaultHourOfDay, 0, 0, offset));
        }

        return result;
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

    private static List<int> SanitizeDaysOfWeek(List<int>? days)
    {
        if (days is null || days.Count == 0)
            return new List<int>();

        return days.Where(d => d is >= 1 and <= 7).Distinct().ToList();
    }

    private static List<int> SanitizeDatesOfMonth(List<int>? dates)
    {
        if (dates is null || dates.Count == 0)
            return new List<int>();

        return dates.Where(d => d is >= 1 and <= 31).Distinct().ToList();
    }

    private static DateTimeOffset GetNextMatchingDayOfWeek(
        List<int> daysOfWeek,
        DateTimeOffset from)
    {
        var targetDow = daysOfWeek
            .Select(d => d == 7 ? DayOfWeek.Sunday : (DayOfWeek)d)
            .ToHashSet();

        for (var i = 0; i <= 8; i++)
        {
            var candidateDay = from.Date.AddDays(i);

            if (!targetDow.Contains(candidateDay.DayOfWeek))
                continue;

            var candidate = new DateTimeOffset(
                candidateDay.Year, candidateDay.Month, candidateDay.Day,
                DefaultHourOfDay, 0, 0, from.Offset);

            if (candidate > from)
                return candidate;
        }

        return from.AddDays(7);
    }

    private sealed class FrequencyConfigDto
    {
        public string? Type { get; set; }
        public List<int>? DaysOfWeek { get; set; }
        public List<int>? DatesOfMonth { get; set; }
        public bool IsLastDayOfMonth { get; set; }
    }
}