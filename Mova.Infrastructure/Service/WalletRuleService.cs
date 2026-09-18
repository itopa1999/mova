using System.Text.Json;
using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Infrastructure.Service;

public sealed class WalletRuleService : IWalletRuleService
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public Task<NextWalletRelease?> GetNextReleaseAsync(
        WalletRule rule,
        DateTimeOffset after,
        CancellationToken cancellationToken = default)
    {
        var configJson = FrequencyConfigHelper.NormalizeConfigJson(rule.FrequencyConfig);

        var nextDate = rule.Frequency switch
        {
            ReleaseFrequency.Once => NextOnce(configJson, after),
            ReleaseFrequency.Daily => NextDaily(configJson, after),
            ReleaseFrequency.Weekly => NextWeekly(configJson, after),
            ReleaseFrequency.Monthly => NextMonthly(configJson, after),
            ReleaseFrequency.Quarterly => NextQuarterly(configJson, after),
            ReleaseFrequency.Yearly => NextYearly(configJson, after),
            ReleaseFrequency.Custom => NextCustom(configJson, after),
            ReleaseFrequency.Hourly => NextHourly(configJson, after),
            _ => null
        };

        // Wallet completion is governed by the remaining locked balance. Do not use EndDate
        // as a hard stop: older wallets may have an end date calculated from a truncated
        // schedule preview, which would otherwise skip their final remainder release.
        if (nextDate is null)
            return Task.FromResult<NextWalletRelease?>(null);

        return Task.FromResult<NextWalletRelease?>(new NextWalletRelease
        {
            ScheduledFor = nextDate.Value,
            Amount = rule.Amount
        });
    }

    // ─────────────────────────────────────────────────────────
    // ONCE
    // ─────────────────────────────────────────────────────────
    private static DateTimeOffset? NextOnce(string json, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<OnceConfig>(json, JsonOptions);
        if (config is null || config.OnceDate == default)
            return null;

        var candidate = FrequencyConfigHelper.ApplyTime(
            config.OnceDate,
            string.IsNullOrWhiteSpace(config.Time) ? "00:00" : config.Time);

        return candidate > after ? candidate : null;
    }

    // ─────────────────────────────────────────────────────────
    // DAILY
    // ─────────────────────────────────────────────────────────
    private static DateTimeOffset? NextDaily(string json, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<DailyConfig>(json, JsonOptions);
        if (config is null)
            return null;

        var days = config.DaysOfWeek.Count == 0
            ? Enumerable.Range(1, 7).ToHashSet()
            : config.DaysOfWeek.ToHashSet();

        var time = string.IsNullOrWhiteSpace(config.Time) ? "00:00" : config.Time;

        // Start from TODAY (not tomorrow) — matches Preview service
        for (var date = after.Date; date <= after.Date.AddDays(7); date = date.AddDays(1))
        {
            if (!days.Contains(ToIsoDay(date.DayOfWeek)))
                continue;

            var candidate = FrequencyConfigHelper.ApplyTime(
                new DateTimeOffset(date, after.Offset),
                time);

            if (candidate > after)
                return candidate;
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────
    // WEEKLY
    // ─────────────────────────────────────────────────────────
    private static DateTimeOffset? NextWeekly(string json, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<WeeklyConfig>(json, JsonOptions);
        if (config is null || config.DaysOfWeek.Count == 0)
            return null;

        var days = config.DaysOfWeek.ToHashSet();
        var time = string.IsNullOrWhiteSpace(config.Time) ? "00:00" : config.Time;

        // Start from TODAY — matches Preview service
        for (var date = after.Date; date <= after.Date.AddDays(7); date = date.AddDays(1))
        {
            if (!days.Contains(ToIsoDay(date.DayOfWeek)))
                continue;

            var candidate = FrequencyConfigHelper.ApplyTime(
                new DateTimeOffset(date, after.Offset),
                time);

            if (candidate > after)
                return candidate;
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────
    // MONTHLY
    // ─────────────────────────────────────────────────────────
    private static DateTimeOffset? NextMonthly(string json, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<MonthlyConfig>(json, JsonOptions);
        if (config is null)
            return null;

        var time = string.IsNullOrWhiteSpace(config.Time) ? "00:00" : config.Time;

        // Walk forward from the CURRENT month — matches Preview service
        var currentMonth = new DateTime(after.Year, after.Month, 1);
        var endSearch = currentMonth.AddMonths(2);

        for (var month = currentMonth; month <= endSearch; month = month.AddMonths(1))
        {
            IEnumerable<int> days;

            if (config.IsLastDayOfMonth && !(config.DatesOfMonth?.Any() ?? false))
            {
                // Only the last day of month
                days = new[] { DateTime.DaysInMonth(month.Year, month.Month) };
            }
            else if (config.DatesOfMonth != null && config.DatesOfMonth.Any())
            {
                // Specific dates
                days = config.DatesOfMonth.OrderBy(x => x);

                // Also include last day if flag set
                if (config.IsLastDayOfMonth)
                {
                    var last = DateTime.DaysInMonth(month.Year, month.Month);
                    days = days.Append(last).Distinct().OrderBy(x => x);
                }
            }
            else
            {
                // Nothing configured
                continue;
            }

            foreach (var day in days)
            {
                var maxDay = DateTime.DaysInMonth(month.Year, month.Month);
                var actualDay = Math.Min(day, maxDay);

                var candidate = FrequencyConfigHelper.ApplyTime(
                    new DateTimeOffset(month.Year, month.Month, actualDay, 0, 0, 0, after.Offset),
                    time);

                if (candidate > after)
                    return candidate;
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────
    // QUARTERLY
    // ─────────────────────────────────────────────────────────
    private static DateTimeOffset? NextQuarterly(string json, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<QuarterlyConfig>(json, JsonOptions);
        if (config is null || config.Months.Count == 0 || config.DaysOfMonth.Count == 0)
            return null;

        var time = string.IsNullOrWhiteSpace(config.Time) ? "00:00" : config.Time;
        var sortedDays = config.DaysOfMonth.OrderBy(x => x).ToList();

        // Walk forward from the CURRENT month — matches Preview service
        var currentMonth = new DateTime(after.Year, after.Month, 1);
        var endSearch = currentMonth.AddMonths(13);

        for (var month = currentMonth; month <= endSearch; month = month.AddMonths(1))
        {
            if (!config.Months.Contains(month.Month))
                continue;

            foreach (var day in sortedDays)
            {
                var maxDay = DateTime.DaysInMonth(month.Year, month.Month);
                var actualDay = Math.Min(day, maxDay);

                var candidate = FrequencyConfigHelper.ApplyTime(
                    new DateTimeOffset(month.Year, month.Month, actualDay, 0, 0, 0, after.Offset),
                    time);

                if (candidate > after)
                    return candidate;
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────
    // YEARLY
    // ─────────────────────────────────────────────────────────
    private static DateTimeOffset? NextYearly(string json, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<YearlyConfig>(json, JsonOptions);
        if (config is null || config.Months.Count == 0 || config.DaysOfMonth.Count == 0)
            return null;

        var time = string.IsNullOrWhiteSpace(config.Time) ? "00:00" : config.Time;
        var sortedMonths = config.Months.OrderBy(x => x).ToList();
        var sortedDays = config.DaysOfMonth.OrderBy(x => x).ToList();

        // Walk forward from the CURRENT year — matches Preview service
        for (var year = after.Year; year <= after.Year + 2; year++)
        {
            foreach (var month in sortedMonths)
            {
                foreach (var day in sortedDays)
                {
                    var maxDay = DateTime.DaysInMonth(year, month);
                    var actualDay = Math.Min(day, maxDay);

                    var candidate = FrequencyConfigHelper.ApplyTime(
                        new DateTimeOffset(year, month, actualDay, 0, 0, 0, after.Offset),
                        time);

                    if (candidate > after)
                        return candidate;
                }
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────
    // CUSTOM
    // ─────────────────────────────────────────────────────────
    private static DateTimeOffset? NextCustom(string json, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<CustomConfig>(json, JsonOptions);
        if (config is null || config.IntervalDays <= 0)
            return null;

        var time = string.IsNullOrWhiteSpace(config.Time) ? "00:00" : config.Time;

        // The preview service anchors the FIRST release on `startDate`,
        // then adds IntervalDays from there. Since we only get `after`,
        // we treat today as the anchor and step forward by interval until
        // we pass `after`.
        var anchorDate = after.Date;

        // Try today first
        var candidate = FrequencyConfigHelper.ApplyTime(
            new DateTimeOffset(anchorDate, after.Offset),
            time);

        if (candidate > after)
            return candidate;

        // Otherwise step forward — bounded to prevent infinite loop
        for (var i = 0; i < 5000; i++)
        {
            anchorDate = anchorDate.AddDays(config.IntervalDays);
            candidate = FrequencyConfigHelper.ApplyTime(
                new DateTimeOffset(anchorDate, after.Offset),
                time);

            if (candidate > after)
                return candidate;
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────
    // HOURLY
    // ─────────────────────────────────────────────────────────
    private static DateTimeOffset? NextHourly(string json, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<HourlyConfig>(json, JsonOptions);
        if (config is null || config.IntervalHours < 1)
            return null;

        // Anchor on the configured time-of-day, on the same offset as `after`.
        var anchorTime = FrequencyConfigHelper.ParseTime(
            string.IsNullOrWhiteSpace(config.Time) ? "00:00" : config.Time);

        var baseAnchor = new DateTimeOffset(
            after.Year, after.Month, after.Day,
            anchorTime.Hours, anchorTime.Minutes, 0,
            after.Offset);

        // If the anchor for today is at/before `after`, roll forward by whole steps.
        var candidate = baseAnchor;
        if (candidate <= after)
        {
            var elapsed = after - baseAnchor;
            var steps = Math.Floor(elapsed.TotalHours / config.IntervalHours) + 1;
            candidate = baseAnchor.AddHours(steps * config.IntervalHours);
        }

        return candidate > after ? candidate : null;
    }

    // ─────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────
    private static int ToIsoDay(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
}