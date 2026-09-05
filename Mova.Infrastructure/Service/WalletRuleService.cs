using System.Text.Json;
using System.Text.Json.Serialization;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Infrastructure.Service;

public sealed class WalletRuleService : IWalletRuleService
{
    public Task<NextWalletRelease?> GetNextReleaseAsync(
        WalletRule rule,
        DateTimeOffset after,
        CancellationToken cancellationToken = default)
    {
        var configJson = FrequencyConfigHelper.NormalizeConfigJson(rule.FrequencyConfig);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        var nextDate = rule.Frequency switch
        {
            ReleaseFrequency.Once => NextOnce(configJson, options, after),
            ReleaseFrequency.Daily => NextDaily(configJson, options, after),
            ReleaseFrequency.Weekly => NextWeekly(configJson, options, after),
            ReleaseFrequency.Monthly => NextMonthly(configJson, options, after),
            ReleaseFrequency.Quarterly => NextQuarterly(configJson, options, after),
            ReleaseFrequency.Yearly => NextYearly(configJson, options, after),
            ReleaseFrequency.Custom => NextCustom(configJson, options, after),
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

    private static DateTimeOffset? NextOnce(string json, JsonSerializerOptions options, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<OnceConfig>(json, options);
        if (config is null || config.OnceDate == default)
            return null;

        return ApplyTime(config.OnceDate, config.Time, after);
    }

    private static DateTimeOffset? NextDaily(string json, JsonSerializerOptions options, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<DailyConfig>(json, options);
        if (config is null)
            return null;

        var days = config.DaysOfWeek.Count == 0
            ? Enumerable.Range(1, 7).ToHashSet()
            : config.DaysOfWeek.ToHashSet();

        for (var date = after.Date.AddDays(1); date <= after.Date.AddDays(8); date = date.AddDays(1))
        {
            if (days.Contains(ToIsoDay(date.DayOfWeek)))
            {
                var candidate = ApplyTime(date, config.Time, after);
                if (candidate is not null)
                    return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? NextWeekly(string json, JsonSerializerOptions options, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<WeeklyConfig>(json, options);
        if (config is null || config.DaysOfWeek.Count == 0)
            return null;

        var days = config.DaysOfWeek.ToHashSet();
        for (var date = after.Date.AddDays(1); date <= after.Date.AddDays(8); date = date.AddDays(1))
        {
            if (days.Contains(ToIsoDay(date.DayOfWeek)))
            {
                var candidate = ApplyTime(date, config.Time, after);
                if (candidate is not null)
                    return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? NextMonthly(string json, JsonSerializerOptions options, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<MonthlyConfig>(json, options);
        if (config is null)
            return null;

        for (var month = new DateTime(after.Year, after.Month, 1).AddMonths(1); month <= after.Date.AddMonths(2); month = month.AddMonths(1))
        {
            IEnumerable<int> days = config.IsLastDayOfMonth
                ? new[] { DateTime.DaysInMonth(month.Year, month.Month) }
                : config.DatesOfMonth.OrderBy(x => x);

            foreach (var day in days)
            {
                var candidate = ApplyTime(new DateTimeOffset(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)), 0, 0, 0, after.Offset), config.Time, after);
                if (candidate is not null)
                    return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? NextQuarterly(string json, JsonSerializerOptions options, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<QuarterlyConfig>(json, options);
        if (config is null)
            return null;

        for (var month = new DateTime(after.Year, after.Month, 1).AddMonths(1); month <= after.Date.AddMonths(13); month = month.AddMonths(1))
        {
            if (!config.Months.Contains(month.Month))
                continue;

            foreach (var day in config.DaysOfMonth.OrderBy(x => x))
            {
                var candidate = ApplyTime(new DateTimeOffset(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)), 0, 0, 0, after.Offset), config.Time, after);
                if (candidate is not null)
                    return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? NextYearly(string json, JsonSerializerOptions options, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<YearlyConfig>(json, options);
        if (config is null)
            return null;

        for (var year = after.Year; year <= after.Year + 2; year++)
        {
            foreach (var month in config.Months.OrderBy(x => x))
            foreach (var day in config.DaysOfMonth.OrderBy(x => x))
            {
                var candidate = ApplyTime(new DateTimeOffset(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)), 0, 0, 0, after.Offset), config.Time, after);
                if (candidate is not null)
                    return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? NextCustom(string json, JsonSerializerOptions options, DateTimeOffset after)
    {
        var config = JsonSerializer.Deserialize<CustomConfig>(json, options);
        if (config is null || config.IntervalDays <= 0)
            return null;

        var candidateDate = after.Date.AddDays(config.IntervalDays);
        return ApplyTime(candidateDate, config.Time, after);
    }

    private static DateTimeOffset? ApplyTime(DateTimeOffset date, string time, DateTimeOffset after)
    {
        var candidate = FrequencyConfigHelper.ApplyTime(date, string.IsNullOrWhiteSpace(time) ? "00:00" : time);
        return candidate > after ? candidate : null;
    }

    private static int ToIsoDay(DayOfWeek dayOfWeek) => dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
}
