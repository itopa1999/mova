using System.Text.Json;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Infrastructure.Services;

public class SchedulePreviewService : ISchedulePreviewService
{
    private readonly IWalletRuleValidator _validator;

    public SchedulePreviewService(IWalletRuleValidator validator)
    {
        _validator = validator;
    }

    public async Task<SchedulePreviewResult> PreviewScheduleAsync(
        decimal targetAmount,
        decimal releaseAmount,
        ReleaseFrequency frequencyType,
        string frequencyConfig,
        DateTimeOffset startDate,
        int maxReleases = 50,
        CancellationToken cancellationToken = default)
    {
        var result = new SchedulePreviewResult
        {
            TargetAmount = targetAmount,
            ReleaseAmount = releaseAmount,
            FrequencyType = frequencyType,
            SampleReleaseDates = new List<ReleaseDatePreview>()
        };

        // 1. Validate basic inputs
        if (targetAmount <= 0)
        {
            result.IsSuccess = false;
            result.Description = "Invalid target amount";
            result.Errors.Add("Target amount must be greater than zero.");
            return result;
        }

        if (targetAmount > 100_000_000)
        {
            result.IsSuccess = false;
            result.Description = "Invalid target amount";
            result.Errors.Add("Target amount cannot exceed ₦100,000,000.");
            return result;
        }

        if (releaseAmount <= 0)
        {
            result.IsSuccess = false;
            result.Description = "Invalid release amount";
            result.Errors.Add("Release amount must be greater than zero.");
            return result;
        }

        if (releaseAmount > 100_000_000)
        {
            result.IsSuccess = false;
            result.Description = "Invalid release amount";
            result.Errors.Add("Release amount cannot exceed ₦100,000,000.");
            return result;
        }

        if (releaseAmount > targetAmount)
        {
            result.IsSuccess = false;
            result.Description = "Invalid release amount";
            result.Errors.Add($"Release amount (₦{releaseAmount:N0}) cannot be greater than target amount (₦{targetAmount:N0}).");
            return result;
        }

        if (Math.Round(targetAmount, 2) != targetAmount)
        {
            result.IsSuccess = false;
            result.Description = "Invalid target amount";
            result.Errors.Add("Target amount cannot have more than 2 decimal places.");
            return result;
        }

        if (Math.Round(releaseAmount, 2) != releaseAmount)
        {
            result.IsSuccess = false;
            result.Description = "Invalid release amount";
            result.Errors.Add("Release amount cannot have more than 2 decimal places.");
            return result;
        }

        var today = DateTimeOffset.UtcNow;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        
        // 2. ONCE specific validations
        if (frequencyType == ReleaseFrequency.Once)
        {
            // Validate OnceDate exists
            var onceConfig = JsonSerializer.Deserialize<OnceConfig>(frequencyConfig, options);
            if (onceConfig == null || onceConfig.OnceDate == default)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("OnceDate is required for Once frequency");
                return result;
            }
            
            // Check if OnceDate is in the past
            if (onceConfig.OnceDate < today)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add($"OnceDate ({onceConfig.OnceDate:MMMM d, yyyy h:mm tt}) cannot be in the past. Please select a future date.");
                return result;
            }
            
            // Check if startDate > OnceDate
            if (startDate > onceConfig.OnceDate)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add($"Start date ({startDate:MMMM d, yyyy h:mm tt}) cannot be greater than OnceDate ({onceConfig.OnceDate:MMMM d, yyyy h:mm tt})");
                return result;
            }
            
            // Check if release amount equals target amount
            if (releaseAmount != targetAmount)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add($"For 'Once' frequency, release amount (₦{releaseAmount:N0}) must equal target amount (₦{targetAmount:N0})");
                return result;
            }
        }

        // 3. DAILY specific validations
        if (frequencyType == ReleaseFrequency.Daily)
        {
            // Check if startDate < Today
            if (startDate < today)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add($"Start date ({startDate:MMMM d, yyyy h:mm tt}) cannot be lesser than Today ({today:MMMM d, yyyy h:mm tt})");
                return result;
            }

            // Validate DaysOfWeek values
            var dailyConfig = JsonSerializer.Deserialize<DailyConfig>(frequencyConfig, options);
            if (dailyConfig?.DaysOfWeek != null && dailyConfig.DaysOfWeek.Any())
            {
                if (dailyConfig.DaysOfWeek.Any(d => d < 1 || d > 7))
                {
                    result.IsSuccess = false;
                    result.Description = "Invalid configuration";
                    result.Errors.Add("DaysOfWeek must contain values between 1 (Monday) and 7 (Sunday)");
                    return result;
                }
            }
        }

        // 4. WEEKLY specific validations
        if (frequencyType == ReleaseFrequency.Weekly)
        {
            // Check if startDate < Today
            if (startDate < today)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add($"Start date ({startDate:MMMM d, yyyy h:mm tt}) cannot be lesser than Today ({today:MMMM d, yyyy h:mm tt})");
                return result;
            }

            // Validate DaysOfWeek values
            var weeklyConfig = JsonSerializer.Deserialize<WeeklyConfig>(frequencyConfig, options);
            if (weeklyConfig?.DaysOfWeek != null && weeklyConfig.DaysOfWeek.Any())
            {
                if (weeklyConfig.DaysOfWeek.Any(d => d < 1 || d > 7))
                {
                    result.IsSuccess = false;
                    result.Description = "Invalid configuration";
                    result.Errors.Add("DaysOfWeek must contain values between 1 (Monday) and 7 (Sunday)");
                    return result;
                }
            }
        }

        // 4. Monthly specific validations
        if (frequencyType == ReleaseFrequency.Monthly)
        {
            // Check if startDate < Today
            if (startDate < today)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add($"Start date ({startDate:MMMM d, yyyy h:mm tt}) cannot be lesser than Today ({today:MMMM d, yyyy h:mm tt})");
                return result;
            }

            // Validate DatesOfMonth values
            var monthlyConfig = JsonSerializer.Deserialize<MonthlyConfig>(frequencyConfig, options);
            if (monthlyConfig?.DatesOfMonth != null && monthlyConfig.DatesOfMonth.Any())
            {
                if (monthlyConfig.DatesOfMonth.Any(d => d < 1 || d > 31))
                {
                    result.IsSuccess = false;
                    result.Description = "Invalid configuration";
                    result.Errors.Add("DatesOfMonth must contain values between 1 and 31");
                    return result;
                }
            }
        }

        // 6. QUARTERLY specific validations
        if (frequencyType == ReleaseFrequency.Quarterly)
        {
            // Check if startDate < Today
            if (startDate < today)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add($"Start date ({startDate:MMMM d, yyyy h:mm tt}) cannot be lesser than Today ({today:MMMM d, yyyy h:mm tt})");
                return result;
            }

            // Validate Quarterly config
            var quarterlyConfig = JsonSerializer.Deserialize<QuarterlyConfig>(frequencyConfig, options);
            if (quarterlyConfig == null)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("Invalid Quarterly configuration");
                return result;
            }
            
            // Validate Months
            if (quarterlyConfig.Months == null || !quarterlyConfig.Months.Any())
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("At least one month must be selected for Quarterly frequency");
                return result;
            }
            
            // Validate Months values (1-12)
            if (quarterlyConfig.Months.Any(m => m < 1 || m > 12))
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("Months must contain values between 1 (January) and 12 (December)");
                return result;
            }
            
            // Validate DaysOfMonth
            if (quarterlyConfig.DaysOfMonth == null || !quarterlyConfig.DaysOfMonth.Any())
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("At least one day must be selected in DaysOfMonth for Quarterly frequency");
                return result;
            }
            
            // Validate DaysOfMonth values (1-31)
            if (quarterlyConfig.DaysOfMonth.Any(d => d < 1 || d > 31))
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("DaysOfMonth must contain values between 1 and 31");
                return result;
            }
        }

        // 7. Yearly specific validations
        if (frequencyType == ReleaseFrequency.Yearly)
        {
            // Check if startDate < Today
            if (startDate < today)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add($"Start date ({startDate:MMMM d, yyyy h:mm tt}) cannot be lesser than Today ({today:MMMM d, yyyy h:mm tt})");
                return result;
            }

            // Validate Yearly config
            var yearlyConfig = JsonSerializer.Deserialize<YearlyConfig>(frequencyConfig, options);
            if (yearlyConfig == null)
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("Invalid Yearly configuration");
                return result;
            }
            
            // Validate Months
            if (yearlyConfig.Months == null || !yearlyConfig.Months.Any())
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("At least one month must be selected for Yearly frequency");
                return result;
            }
            
            // Validate Months values (1-12)
            if (yearlyConfig.Months.Any(m => m < 1 || m > 12))
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("Months must contain values between 1 (January) and 12 (December)");
                return result;
            }
            
            // Validate DaysOfMonth
            if (yearlyConfig.DaysOfMonth == null || !yearlyConfig.DaysOfMonth.Any())
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("At least one day must be selected in DaysOfMonth for Yearly frequency");
                return result;
            }
            
            // Validate DaysOfMonth values (1-31)
            if (yearlyConfig.DaysOfMonth.Any(d => d < 1 || d > 31))
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("DaysOfMonth must contain values between 1 and 31");
                return result;
            }
        }

        // 5. Validate the config using the validator
        var validationResult = await _validator.ValidateConfigAsync(frequencyType, frequencyConfig, cancellationToken);

        if (!validationResult.IsValid)
        {
            result.IsSuccess = false;
            result.Errors = validationResult.Errors;
            result.Warnings = validationResult.Warnings;
            result.Description = "Invalid configuration";
            return result;
        }

        result.Warnings = validationResult.Warnings;

        try
        {
            // 6. Deserialize config based on frequency type
            var config = FrequencyConfigHelper.DeserializeConfig(frequencyConfig, frequencyType);

            if (config == null || string.IsNullOrEmpty(config.Time))
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("Time is required. Please provide time in HH:mm format (e.g., 09:30, 14:45)");
                return result;
            }

            // Validate time format
            if (!IsValidTimeFormat(config.Time))
            {
                result.IsSuccess = false;
                result.Description = "Invalid configuration";
                result.Errors.Add("Time must be in HH:mm format (e.g., 09:30, 14:45) with hours 00-23 and minutes 00-59");
                return result;
            }
            
            // 7. Calculate releases
            var regularReleases = (int)Math.Floor(targetAmount / releaseAmount);
            var regularTotal = regularReleases * releaseAmount;
            var remainder = targetAmount - regularTotal;

            decimal regularReleaseAmount = releaseAmount;
            decimal finalReleaseAmount = remainder > 0 ? remainder : releaseAmount;
            var totalReleases = remainder > 0 ? regularReleases + 1 : regularReleases;

            if (frequencyType == ReleaseFrequency.Once)
            {
                regularReleaseAmount = targetAmount;
                finalReleaseAmount = targetAmount;
                totalReleases = 1;
                regularReleases = 1;
            }

            result.RegularReleaseAmount = regularReleaseAmount;
            result.FinalReleaseAmount = finalReleaseAmount;
            result.TotalReleases = totalReleases;
            result.TotalAmount = targetAmount;

            // 8. Generate release dates with amounts
            var releaseDates = GenerateReleaseDatesWithAmounts(
                frequencyType,
                config,
                startDate,
                regularReleaseAmount,
                finalReleaseAmount,
                totalReleases,
                maxReleases);

            result.FirstReleaseDate = releaseDates.FirstOrDefault()?.Date ?? startDate;
            result.ComputedEndDate = releaseDates.LastOrDefault()?.Date ?? startDate;
            result.SampleReleaseDates = releaseDates;

            // Calculate time to reach target
            if (result.ComputedEndDate != default && startDate != default)
            {
                var timeSpan = result.ComputedEndDate - startDate;
                result.WeeksToReachTarget = (int)(timeSpan.TotalDays / 7);
                result.MonthsToReachTarget = (int)(timeSpan.TotalDays / 30);
            }

            // 9. Set description
            result.Description = FrequencyConfigHelper.GetDescription(frequencyType, frequencyConfig);
            result.IsSuccess = true;

            // Add warning if too many releases
            if (totalReleases > 365)
            {
                result.Warnings.Add($"Your schedule requires {totalReleases} releases which will take approximately {result.WeeksToReachTarget} weeks to complete.");
            }

            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.Errors.Add($"Error generating schedule preview: {ex.Message}");
            return result;
        }
    }

    private List<ReleaseDatePreview> GenerateReleaseDatesWithAmounts(
        ReleaseFrequency frequencyType,
        FrequencyConfigBase config,
        DateTimeOffset startDate,
        decimal regularAmount,
        decimal finalAmount,
        int totalReleases,
        int maxReleases)
    {
        var releaseDates = new List<ReleaseDatePreview>();
        var generatedDates = new List<DateTimeOffset>();

        switch (frequencyType)
        {
            case ReleaseFrequency.Once:
                generatedDates = GenerateOnceDates(config as OnceConfig, startDate, totalReleases);
                break;

            case ReleaseFrequency.Daily:
                generatedDates = GenerateDailyDates(config as DailyConfig, startDate, totalReleases, maxReleases);
                break;

            case ReleaseFrequency.Weekly:
                generatedDates = GenerateWeeklyDates(config as WeeklyConfig, startDate, totalReleases, maxReleases);
                break;

            case ReleaseFrequency.Monthly:
                generatedDates = GenerateMonthlyDates(config as MonthlyConfig, startDate, totalReleases, maxReleases);
                break;

            case ReleaseFrequency.Quarterly:
                generatedDates = GenerateQuarterlyDates(config as QuarterlyConfig, startDate, totalReleases, maxReleases);
                break;

            case ReleaseFrequency.Yearly:
                generatedDates = GenerateYearlyDates(config as YearlyConfig, startDate, totalReleases, maxReleases);
                break;

            case ReleaseFrequency.Custom:
                generatedDates = GenerateCustomDates(config as CustomConfig, startDate, totalReleases, maxReleases);
                break;
        }

        // Build release previews with amounts
        decimal cumulative = 0;
        for (int i = 0; i < generatedDates.Count && i < maxReleases; i++)
        {
            var isLastRelease = (i == generatedDates.Count - 1);
            var amount = isLastRelease ? finalAmount : regularAmount;
            
            if (frequencyType == ReleaseFrequency.Once)
            {
                amount = finalAmount;
            }

            cumulative += amount;

            releaseDates.Add(new ReleaseDatePreview
            {
                Date = generatedDates[i],
                Amount = amount,
                ReleaseNumber = i + 1,
                CumulativeAmount = cumulative
            });
        }

        return releaseDates;
    }

    #region Date Generation Methods

    private List<DateTimeOffset> GenerateOnceDates(
        OnceConfig config,
        DateTimeOffset startDate,
        int totalReleases)
    {
        var dates = new List<DateTimeOffset>();
        if (config?.OnceDate != null)
        {
            // Get time from config (default to 00:00 if not provided)
            var timeString = config.Time ?? "00:00";
            
            // Apply time to the date
            var releaseDate = FrequencyConfigHelper.ApplyTime(config.OnceDate, timeString);
            dates.Add(releaseDate);
        }
        return dates;
    }

    private List<DateTimeOffset> GenerateDailyDates(
        DailyConfig config,
        DateTimeOffset startDate,
        int totalReleases,
        int maxReleases)
    {
        var dates = new List<DateTimeOffset>();
        if (config == null || config.DaysOfWeek == null)
            return dates;

        // If DaysOfWeek is empty, treat as every day
        var daysOfWeek = config.DaysOfWeek.Any() ? config.DaysOfWeek : new List<int> { 1, 2, 3, 4, 5, 6, 7 };
        
        // Get time from config (default to 00:00 if not provided)
        var timeString = config.Time ?? "00:00";
        
        var currentDate = startDate.Date;
        var releasesAdded = 0;
        var daysChecked = 0;
        var maxDays = 365 * 100; // 100 years max to prevent infinite loop

        while (releasesAdded < totalReleases && releasesAdded < maxReleases && daysChecked < maxDays)
        {
            var dayOfWeek = (int)currentDate.DayOfWeek;
            var configDay = dayOfWeek == 0 ? 7 : dayOfWeek; // Convert Sunday from 0 to 7

            // Check if we should release on this day
            if (daysOfWeek.Contains(configDay))
            {
                // Apply time to the date
                var releaseDate = FrequencyConfigHelper.ApplyTime(currentDate, timeString);
                dates.Add(releaseDate);
                releasesAdded++;
            }

            currentDate = currentDate.AddDays(1);
            daysChecked++;
        }

        return dates;
    }

    private List<DateTimeOffset> GenerateWeeklyDates(
        WeeklyConfig config,
        DateTimeOffset startDate,
        int totalReleases,
        int maxReleases)
    {
        var dates = new List<DateTimeOffset>();
        if (config == null || config.DaysOfWeek == null || !config.DaysOfWeek.Any())
            return dates;

        // Sort days of week for consistent ordering
        var sortedDays = config.DaysOfWeek.OrderBy(d => d).ToList();
        
        // Get time from config (default to 00:00 if not provided)
        var timeString = config.Time ?? "00:00";
        
        var currentDate = startDate.Date;
        var releasesAdded = 0;
        var daysChecked = 0;
        var maxDays = 365 * 100; // 100 years max to prevent infinite loop

        while (releasesAdded < totalReleases && releasesAdded < maxReleases && daysChecked < maxDays)
        {
            var dayOfWeek = (int)currentDate.DayOfWeek;
            var configDay = dayOfWeek == 0 ? 7 : dayOfWeek; // Convert Sunday from 0 to 7

            // Check if we should release on this day
            if (sortedDays.Contains(configDay))
            {
                // Apply time to the date
                var releaseDate = FrequencyConfigHelper.ApplyTime(currentDate, timeString);
                dates.Add(releaseDate);
                releasesAdded++;
            }

            currentDate = currentDate.AddDays(1);
            daysChecked++;
        }

        return dates;
    }

    private List<DateTimeOffset> GenerateMonthlyDates(
        MonthlyConfig config,
        DateTimeOffset startDate,
        int totalReleases,
        int maxReleases)
    {
        var dates = new List<DateTimeOffset>();
        if (config == null)
            return dates;

        // Get time from config (default to 00:00 if not provided)
        var timeString = config.Time ?? "00:00";
        
        var currentDate = startDate.Date;
        var releasesAdded = 0;
        var monthsChecked = 0;
        var maxMonths = 12 * 100; // 100 years max

        while (releasesAdded < totalReleases && releasesAdded < maxReleases && monthsChecked < maxMonths)
        {
            if (config.IsLastDayOfMonth)
            {
                // Release on last day of month
                var lastDay = DateTime.DaysInMonth(currentDate.Year, currentDate.Month);
                var releaseDate = new DateTimeOffset(currentDate.Year, currentDate.Month, lastDay, 0, 0, 0, TimeSpan.Zero);
                
                // Apply time
                releaseDate = FrequencyConfigHelper.ApplyTime(releaseDate, timeString);

                if (releaseDate >= startDate)
                {
                    dates.Add(releaseDate);
                    releasesAdded++;
                }
            }
            else if (config.DatesOfMonth != null && config.DatesOfMonth.Any())
            {
                // Release on specific dates of month
                foreach (var day in config.DatesOfMonth.OrderBy(d => d))
                {
                    if (releasesAdded >= totalReleases || releasesAdded >= maxReleases)
                        break;

                    var maxDay = DateTime.DaysInMonth(currentDate.Year, currentDate.Month);
                    var actualDay = Math.Min(day, maxDay);
                    var releaseDate = new DateTimeOffset(currentDate.Year, currentDate.Month, actualDay, 0, 0, 0, TimeSpan.Zero);
                    
                    // Apply time
                    releaseDate = FrequencyConfigHelper.ApplyTime(releaseDate, timeString);

                    if (releaseDate >= startDate)
                    {
                        dates.Add(releaseDate);
                        releasesAdded++;
                    }
                }
            }
            else
            {
                // No dates configured, break
                break;
            }

            currentDate = currentDate.AddMonths(1);
            monthsChecked++;
        }

        return dates;
    }

    private List<DateTimeOffset> GenerateQuarterlyDates(
        QuarterlyConfig config,
        DateTimeOffset startDate,
        int totalReleases,
        int maxReleases)
    {
        var dates = new List<DateTimeOffset>();
        if (config == null || config.Months == null || !config.Months.Any())
            return dates;

        if (config.DaysOfMonth == null || !config.DaysOfMonth.Any())
            return dates;

         var sortedDays = config.DaysOfMonth.OrderBy(d => d).ToList();

        // Get time from config (default to 00:00 if not provided)
        var timeString = config.Time ?? "00:00";
        
        var currentDate = startDate.Date;
        var releasesAdded = 0;
        var monthsChecked = 0;
        var maxMonths = 12 * 100; // 100 years max

        while (releasesAdded < totalReleases && releasesAdded < maxReleases && monthsChecked < maxMonths)
        {
            if (config.Months.Contains(currentDate.Month))
            {
                foreach (var day in sortedDays)
                {
                    if (releasesAdded >= totalReleases || releasesAdded >= maxReleases)
                        break;

                    var maxDay = DateTime.DaysInMonth(currentDate.Year, currentDate.Month);
                    var actualDay = Math.Min(day, maxDay);
                    var releaseDate = new DateTimeOffset(currentDate.Year, currentDate.Month, actualDay, 0, 0, 0, TimeSpan.Zero);
                    
                    // Apply time
                    releaseDate = FrequencyConfigHelper.ApplyTime(releaseDate, timeString);

                    if (releaseDate >= startDate)
                    {
                        dates.Add(releaseDate);
                        releasesAdded++;
                    }
                }
            }

            currentDate = currentDate.AddMonths(1);
            monthsChecked++;
        }

        return dates;
    }

    private List<DateTimeOffset> GenerateYearlyDates(
        YearlyConfig config,
        DateTimeOffset startDate,
        int totalReleases,
        int maxReleases)
    {
        var dates = new List<DateTimeOffset>();
        if (config == null)
            return dates;

        if (config.Months == null || !config.Months.Any())
            return dates;

        if (config.DaysOfMonth == null || !config.DaysOfMonth.Any())
            return dates;

        var sortedMonths = config.Months.OrderBy(m => m).ToList();
        var sortedDays = config.DaysOfMonth.OrderBy(d => d).ToList();

        // Get time from config (default to 00:00 if not provided)
        var timeString = config.Time ?? "00:00";
        
        var currentYear = startDate.Year;
        var releasesAdded = 0;
        var yearsChecked = 0;
        var maxYears = 100; // 100 years max

        while (releasesAdded < totalReleases && releasesAdded < maxReleases && yearsChecked < maxYears)
        {
            foreach (var month in sortedMonths)
            {
                if (releasesAdded >= totalReleases || releasesAdded >= maxReleases)
                    break;
                
                var yearToProcess = currentYear;

                if (yearToProcess < startDate.Year)
                    continue;

                foreach (var day in sortedDays)
                {
                    if (releasesAdded >= totalReleases || releasesAdded >= maxReleases)
                        break;

                    var maxDay = DateTime.DaysInMonth(yearToProcess, month);
                    var actualDay = Math.Min(day, maxDay);
                    var releaseDate = new DateTimeOffset(yearToProcess, month, actualDay, 0, 0, 0, TimeSpan.Zero);
                    
                    // Apply time
                    releaseDate = FrequencyConfigHelper.ApplyTime(releaseDate, timeString);

                    if (releaseDate >= startDate)
                    {
                        dates.Add(releaseDate);
                        releasesAdded++;
                    }
                }
            }

            currentYear++;
            yearsChecked++;
        }
        return dates;
    }

    private List<DateTimeOffset> GenerateCustomDates(
        CustomConfig config,
        DateTimeOffset startDate,
        int totalReleases,
        int maxReleases)
    {
        var dates = new List<DateTimeOffset>();
        if (config == null || config.IntervalDays <= 0)
            return dates;

        // Get time from config (default to 00:00 if not provided)
        var timeString = config.Time ?? "00:00";
        
        var currentDate = startDate.Date;
        var releasesAdded = 0;

        while (releasesAdded < totalReleases && releasesAdded < maxReleases)
        {
            // Apply time to the date
            var releaseDate = FrequencyConfigHelper.ApplyTime(currentDate, timeString);
            dates.Add(releaseDate);
            releasesAdded++;
            
            currentDate = currentDate.AddDays(config.IntervalDays);
        }

        return dates;
    }

    #endregion

    private bool IsValidTimeFormat(string time)
    {
        if (string.IsNullOrEmpty(time))
            return false;
        
        if (!TimeSpan.TryParse(time, out var parsedTime))
            return false;
        
        return parsedTime.TotalHours >= 0 && parsedTime.TotalHours < 24;
    }
}