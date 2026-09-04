
using System.Text.Json.Serialization;
using Mova.Domain.Enums;

namespace Mova.Domain.ValueObjects;

// Base config - all types share these
public class FrequencyConfigBase
{
    [JsonPropertyName("type")]
    public ReleaseFrequency Type { get; set; }

    [JsonPropertyName("time")]
    public string Time { get; set; }
}

// Once: Release once on a specific date
public class OnceConfig : FrequencyConfigBase
{
    [JsonPropertyName("onceDate")]
    public DateTime OnceDate { get; set; }
}

// Daily: Release every day (or specific days of week)
public class DailyConfig : FrequencyConfigBase
{
    [JsonPropertyName("daysOfWeek")]
    public List<int> DaysOfWeek { get; set; } = new(); // Monday=1, Sunday=7
    // If empty or all 1-7, means every day
}

// Weekly: Release on specific days of week
public class WeeklyConfig : FrequencyConfigBase
{
    [JsonPropertyName("daysOfWeek")]
    public List<int> DaysOfWeek { get; set; } = new(); // Monday=1, Sunday=7
}

// Monthly: Release on specific dates of month
public class MonthlyConfig : FrequencyConfigBase
{
    [JsonPropertyName("datesOfMonth")]
    public List<int> DatesOfMonth { get; set; } = new(); // 1-31
    [JsonPropertyName("isLastDayOfMonth")]
    public bool IsLastDayOfMonth { get; set; } // If true, DatesOfMonth is ignored
}

// Quarterly: Release every 3 months
public class QuarterlyConfig : FrequencyConfigBase
{
    [JsonPropertyName("months")]
    public List<int> Months { get; set; } = new(); // 1=Jan, 12=Dec
    [JsonPropertyName("daysOfMonth")]
    public List<int> DaysOfMonth { get; set; } = new(); // 1-31 
}

// Yearly: Release once a year
public class YearlyConfig : FrequencyConfigBase
{
    [JsonPropertyName("months")]
    public List<int> Months { get; set; } = new(); // 1=Jan, 12=Dec

    [JsonPropertyName("daysOfMonth")]
    public List<int> DaysOfMonth { get; set; } = new(); // 1-31
}

// Custom: Release every X days
public class CustomConfig : FrequencyConfigBase
{
    [JsonPropertyName("intervalDays")]
    public int IntervalDays { get; set; }
}