
using Mova.Domain.Enums;

namespace Mova.Domain.ValueObjects;

// Base config - all types share these
public class FrequencyConfigBase
{
    public ReleaseFrequency Type { get; set; }

    public string Time { get; set; } = string.Empty;
}

// Once: Release once on a specific date
public class OnceConfig : FrequencyConfigBase
{
    public DateTime OnceDate { get; set; }
}

// Daily: Release every day (or specific days of week)
public class DailyConfig : FrequencyConfigBase
{
    public List<int> DaysOfWeek { get; set; } = new(); // Monday=1, Sunday=7
    // If empty or all 1-7, means every day
}

// Weekly: Release on specific days of week
public class WeeklyConfig : FrequencyConfigBase
{
    public List<int> DaysOfWeek { get; set; } = new(); // Monday=1, Sunday=7
}

// Monthly: Release on specific dates of month
public class MonthlyConfig : FrequencyConfigBase
{
    public List<int> DatesOfMonth { get; set; } = new(); // 1-31
    public bool IsLastDayOfMonth { get; set; } // If true, DatesOfMonth is ignored
}

// Quarterly: Release every 3 months
public class QuarterlyConfig : FrequencyConfigBase
{
    public List<int> Months { get; set; } = new(); // 1=Jan, 12=Dec
    public List<int> DaysOfMonth { get; set; } = new(); // 1-31 
}

// Yearly: Release once a year
public class YearlyConfig : FrequencyConfigBase
{
    public List<int> Months { get; set; } = new(); // 1=Jan, 12=Dec

    public List<int> DaysOfMonth { get; set; } = new(); // 1-31
}

// Custom: Release every X days
public class CustomConfig : FrequencyConfigBase
{
    public int IntervalDays { get; set; }
}