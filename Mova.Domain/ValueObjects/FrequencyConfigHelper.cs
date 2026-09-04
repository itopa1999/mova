using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Mova.Domain.Enums;

namespace Mova.Domain.ValueObjects;

public static class FrequencyConfigHelper
{
    public static string NormalizeConfigJson(string json)
    {
        JsonNode? node;

        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        if (node is not JsonObject config)
            return json;

        var normalized = new JsonObject();

        foreach (var property in config)
        {
            var propertyName = property.Key.ToLowerInvariant() switch
            {
                "type" => "type",
                "time" => "time",
                "oncedate" => "onceDate",
                "daysofweek" => "daysOfWeek",
                "datesofmonth" => "datesOfMonth",
                "islastdayofmonth" => "isLastDayOfMonth",
                "months" => "months",
                "daysofmonth" => "daysOfMonth",
                "intervaldays" => "intervalDays",
                _ => property.Key
            };

            normalized[propertyName] = property.Value?.DeepClone();
        }

        return normalized.ToJsonString();
    }

    // Serialize config to JSON
    public static string SerializeConfig(FrequencyConfigBase config)
    {
        return JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
    
    // Deserialize JSON to specific config type
    public static FrequencyConfigBase DeserializeConfig(string json, ReleaseFrequency type)
    {
        json = NormalizeConfigJson(json);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return type switch
        {
            ReleaseFrequency.Once => JsonSerializer.Deserialize<OnceConfig>(json, options) ?? new OnceConfig(),
            ReleaseFrequency.Daily => JsonSerializer.Deserialize<DailyConfig>(json, options) ?? new DailyConfig(),
            ReleaseFrequency.Weekly => JsonSerializer.Deserialize<WeeklyConfig>(json, options) ?? new WeeklyConfig(),
            ReleaseFrequency.Monthly => JsonSerializer.Deserialize<MonthlyConfig>(json, options) ?? new MonthlyConfig(),
            ReleaseFrequency.Quarterly => JsonSerializer.Deserialize<QuarterlyConfig>(json, options) ?? new QuarterlyConfig(),
            ReleaseFrequency.Yearly => JsonSerializer.Deserialize<YearlyConfig>(json, options) ?? new YearlyConfig(),
            ReleaseFrequency.Custom => JsonSerializer.Deserialize<CustomConfig>(json, options) ?? new CustomConfig(),
            _ => throw new ArgumentException($"Unsupported frequency type: {type}")
        };
    }
    
    // Get human-readable description
    public static string GetDescription(ReleaseFrequency type, string configJson)
    {
        var config = DeserializeConfig(configJson, type);
        
        return type switch
        {
            ReleaseFrequency.Once => $"Once on {((OnceConfig)config).OnceDate:MMMM d, yyyy}",
            ReleaseFrequency.Daily => GetDailyDescription((DailyConfig)config),
            ReleaseFrequency.Weekly => GetWeeklyDescription((WeeklyConfig)config),
            ReleaseFrequency.Monthly => GetMonthlyDescription((MonthlyConfig)config),
            ReleaseFrequency.Quarterly => GetQuarterlyDescription((QuarterlyConfig)config),
            ReleaseFrequency.Yearly => GetYearlyDescription((YearlyConfig)config),
            ReleaseFrequency.Custom => $"Every {((CustomConfig)config).IntervalDays} days",
            _ => "Unknown schedule"
        };
    }
    
    private static string GetDailyDescription(DailyConfig config)
    {
        if (config.DaysOfWeek.Count == 0 || config.DaysOfWeek.Count == 7)
            return "Every day";
        
        if (config.DaysOfWeek.Count == 5 && 
            config.DaysOfWeek.SequenceEqual(new[] { 1, 2, 3, 4, 5 }))
            return "Every weekday (Mon-Fri)";
        
        if (config.DaysOfWeek.Count == 3 && 
            config.DaysOfWeek.SequenceEqual(new[] { 5, 6, 7 }))
            return "Every weekend (Fri-Sun)";
        
        return $"Every {string.Join(", ", config.DaysOfWeek.Select(DayOfWeekToString))}";
    }
    
    private static string GetWeeklyDescription(WeeklyConfig config)
    {
        if (config.DaysOfWeek.Count == 0)
            return "Every week (no days selected)";
        
        return $"Every {string.Join(", ", config.DaysOfWeek.Select(DayOfWeekToString))}";
    }
    
    private static string GetMonthlyDescription(MonthlyConfig config)
    {
        if (config.IsLastDayOfMonth)
            return "Last day of every month";
        
        if (config.DatesOfMonth.Count == 1)
            return $"{OrdinalSuffix(config.DatesOfMonth[0])} of every month";
        
        return $"{string.Join(", ", config.DatesOfMonth.Select(d => OrdinalSuffix(d)))} of every month";
    }
    
    private static string GetQuarterlyDescription(QuarterlyConfig config)
    {
        if (config.Months == null || !config.Months.Any())
            return "Quarterly (no months selected)";
        
        if (config.DaysOfMonth == null || !config.DaysOfMonth.Any())
            return "Quarterly (no days selected)";
        
        var monthNames = config.Months.Select(m => MonthToString(m));
        var dayNames = string.Join(", ", config.DaysOfMonth.Select(d => OrdinalSuffix(d)));
        
        return $"{string.Join(", ", monthNames)} on the {dayNames}";
    }
    
    private static string GetYearlyDescription(YearlyConfig config)
    {
        if (config.Months == null || !config.Months.Any())
            return "Yearly (no months selected)";
        
        if (config.DaysOfMonth == null || !config.DaysOfMonth.Any())
            return "Yearly (no days selected)";
        
        var monthNames = config.Months.Select(m => MonthToString(m));
        var dayNames = string.Join(", ", config.DaysOfMonth.Select(d => OrdinalSuffix(d)));
        
        return $"Yearly on {string.Join(", ", monthNames)} {dayNames} at {config.Time}";
    }    
    
    private static string DayOfWeekToString(int day)
    {
        return day switch
        {
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            6 => "Saturday",
            7 => "Sunday",
            _ => "Unknown"
        };
    }
    
    private static string MonthToString(int month)
    {
        return month switch
        {
            1 => "January",
            2 => "February",
            3 => "March",
            4 => "April",
            5 => "May",
            6 => "June",
            7 => "July",
            8 => "August",
            9 => "September",
            10 => "October",
            11 => "November",
            12 => "December",
            _ => "Unknown"
        };
    }
    
    private static string OrdinalSuffix(int number)
    {
        if (number <= 0) return number.ToString();
        
        return number switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ when number % 10 == 1 && number % 100 != 11 => $"{number}st",
            _ when number % 10 == 2 && number % 100 != 12 => $"{number}nd",
            _ when number % 10 == 3 && number % 100 != 13 => $"{number}rd",
            _ => $"{number}th"
        };
    }

    public static TimeSpan ParseTime(string timeString)
    {
        if (string.IsNullOrEmpty(timeString))
            return TimeSpan.Zero;
            
        if (TimeSpan.TryParse(timeString, out var time))
        {
            // Validate time is within 00:00 to 23:59
            if (time.TotalHours < 0 || time.TotalHours >= 24)
                throw new ArgumentException("Time must be between 00:00 and 23:59");
            return time;
        }
        
        throw new ArgumentException("Time must be in HH:mm format (e.g., 09:30, 14:45)");
    }

    public static DateTimeOffset ApplyTime(DateTimeOffset date, string timeString)
    {
        var time = ParseTime(timeString);
        return new DateTimeOffset(
            date.Year,
            date.Month,
            date.Day,
            time.Hours,
            time.Minutes,
            0,
            date.Offset
        );
    }

}