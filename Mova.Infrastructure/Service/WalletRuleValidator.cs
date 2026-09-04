using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Persistence;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Infrastructure.Service;

public class WalletRuleValidator : IWalletRuleValidator
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly IUnitOfWork _unitOfWork;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public WalletRuleValidator(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // ================================================
    // VALIDATE FOR NEW WALLET
    // ================================================

    public async Task<ValidationResult> ValidateForNewWalletAsync(WalletRule rule, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        return result;
    }

    // ================================================
    // VALIDATE FOR EXISTING WALLET
    // ================================================

    public async Task<ValidationResult> ValidateForExistingWalletAsync(WalletRule rule, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        // 2. Wallet existence validation
        await ValidateWalletExistsAsync(rule, result, cancellationToken);

        // 3. Duplicate rule validation
        await ValidateDuplicateAsync(rule, result, cancellationToken);

        // 4. Frequency-specific validation
        var configResult = await ValidateConfigAsync(rule.Frequency, rule.FrequencyConfig, cancellationToken);
        result.Errors.AddRange(configResult.Errors);
        result.Warnings.AddRange(configResult.Warnings);

        return result;
    }

    // ================================================
    // VALIDATE ONLY CONFIG
    // ================================================

    public async Task<ValidationResult> ValidateConfigAsync(ReleaseFrequency type, string configJson, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(configJson))
        {
            result.AddError("Frequency configuration is required.");
            return result;
        }

        configJson = FrequencyConfigHelper.NormalizeConfigJson(configJson);

        try
        {
            switch (type)
            {
                case ReleaseFrequency.Once:
                    ValidateOnceConfig(configJson, result);
                    break;
                case ReleaseFrequency.Daily:
                    ValidateDailyConfig(configJson, result);
                    break;
                case ReleaseFrequency.Weekly:
                    ValidateWeeklyConfig(configJson, result);
                    break;
                case ReleaseFrequency.Monthly:
                    ValidateMonthlyConfig(configJson, result);
                    break;
                case ReleaseFrequency.Quarterly:
                    ValidateQuarterlyConfig(configJson, result);
                    break;
                case ReleaseFrequency.Yearly:
                    ValidateYearlyConfig(configJson, result);
                    break;
                case ReleaseFrequency.Custom:
                    ValidateCustomConfig(configJson, result);
                    break;
                default:
                    result.AddError($"Unknown frequency type: {type}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            result.AddError($"Invalid JSON configuration: {ex.Message}");
        }
        catch (Exception ex)
        {
            result.AddError($"Error validating configuration: {ex.Message}");
        }

        return result;
    }


    #region Wallet & Duplicate Validations (For Existing Wallet Only)

    private async Task ValidateWalletExistsAsync(WalletRule rule, ValidationResult result, CancellationToken cancellationToken)
    {
        if (rule.WalletId == 0)
        {
            result.AddError("Wallet ID is required.");
            return;
        }

        var wallet = await _unitOfWork.Query<Wallet>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == rule.WalletId, cancellationToken);

        if (wallet == null)
            result.AddError($"Wallet with ID '{rule.WalletId}' does not exist.");
    }

    private async Task ValidateDuplicateAsync(WalletRule rule, ValidationResult result, CancellationToken cancellationToken)
    {
        // Only check for existing rules if wallet exists
        if (rule.WalletId == 0)
            return;

        var existingRules = await _unitOfWork.Query<WalletRule>()
            .Where(r => r.WalletId == rule.WalletId)
            .ToListAsync(cancellationToken);

        if (existingRules == null || !existingRules.Any())
            return;

        foreach (var existing in existingRules)
        {
            if (existing.Id == rule.Id) continue; // Skip self (for updates)

            if (existing.Frequency != rule.Frequency)
                continue;

            if (AreConfigsEqual(existing.FrequencyConfig, rule.FrequencyConfig, rule.Frequency))
            {
                result.AddWarning($"Duplicate schedule detected. You already have a similar rule with ID: {existing.Id}");
            }
        }
    }

    private bool AreConfigsEqual(string config1, string config2, ReleaseFrequency type)
    {
        if (string.IsNullOrEmpty(config1) || string.IsNullOrEmpty(config2))
            return false;

        try
        {
            var obj1 = JsonSerializer.Deserialize<JsonElement>(config1, JsonOptions);
            var obj2 = JsonSerializer.Deserialize<JsonElement>(config2, JsonOptions);
            return string.Equals(
                JsonSerializer.Serialize(obj1),
                JsonSerializer.Serialize(obj2),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Frequency-Specific Validations

    private void ValidateOnceConfig(string configJson, ValidationResult result)
    {
        var config = JsonSerializer.Deserialize<OnceConfig>(configJson, JsonOptions);

        if (config == null)
        {
            result.AddError("Invalid Once configuration.");
            return;
        }

        if (config.OnceDate == default)
            result.AddError("Release date is required for one-time schedule.");

        if (config.OnceDate < DateTimeOffset.UtcNow.Date)
            result.AddWarning("The one-time release date is in the past. It will be processed immediately.");

        if (config.OnceDate > DateTimeOffset.UtcNow.AddYears(10))
            result.AddWarning("Release date is more than 10 years in the future. Is this correct?");
    }

    private void ValidateDailyConfig(string configJson, ValidationResult result)
    {
        var config = JsonSerializer.Deserialize<DailyConfig>(configJson, JsonOptions);

        if (config == null)
        {
            result.AddError("Invalid Daily configuration.");
            return;
        }

        if (config.DaysOfWeek == null || config.DaysOfWeek.Count == 0)
        {
            result.AddError("At least one day of the week must be selected for daily schedule.");
            return;
        }

        var invalidDays = config.DaysOfWeek.Where(d => d < 1 || d > 7).ToList();
        if (invalidDays.Any())
            result.AddError($"Invalid days of week: {string.Join(", ", invalidDays)}. Must be between 1 (Monday) and 7 (Sunday).");

        var duplicates = config.DaysOfWeek.GroupBy(d => d).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Any())
            result.AddWarning($"Duplicate days detected: {string.Join(", ", duplicates)}. These will be removed.");

        if (config.DaysOfWeek.Count == 7)
        {
            result.AddWarning("Every day selected. Consider if this is intentional.");
        }
        else if (config.DaysOfWeek.Count == 5 &&
                 config.DaysOfWeek.OrderBy(d => d).SequenceEqual(new[] { 1, 2, 3, 4, 5 }))
        {
            // Weekdays only - valid
        }
        else if (config.DaysOfWeek.Count == 3 &&
                 config.DaysOfWeek.OrderBy(d => d).SequenceEqual(new[] { 5, 6, 7 }))
        {
            // Weekends only - valid
        }
        else if (config.DaysOfWeek.Count < 3)
        {
            result.AddWarning($"Only {config.DaysOfWeek.Count} day(s) selected. Consider using Weekly frequency instead.");
        }
    }

    private void ValidateWeeklyConfig(string configJson, ValidationResult result)
    {
        var config = JsonSerializer.Deserialize<WeeklyConfig>(configJson, JsonOptions);

        if (config == null)
        {
            result.AddError("Invalid Weekly configuration.");
            return;
        }

        if (config.DaysOfWeek == null || config.DaysOfWeek.Count == 0)
        {
            result.AddError("At least one day of the week must be selected for weekly schedule.");
            return;
        }

        var invalidDays = config.DaysOfWeek.Where(d => d < 1 || d > 7).ToList();
        if (invalidDays.Any())
            result.AddError($"Invalid days of week: {string.Join(", ", invalidDays)}. Must be between 1 (Monday) and 7 (Sunday).");

        var duplicates = config.DaysOfWeek.GroupBy(d => d).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Any())
            result.AddWarning($"Duplicate days detected: {string.Join(", ", duplicates)}. These will be removed.");

        if (config.DaysOfWeek.Count == 7)
            result.AddWarning("All days selected. Consider using Daily frequency instead.");

        if (config.DaysOfWeek.Count > 4)
            result.AddWarning($"You selected {config.DaysOfWeek.Count} days. Consider if Daily frequency would be more appropriate.");
    }

    private void ValidateMonthlyConfig(string configJson, ValidationResult result)
    {
        var config = JsonSerializer.Deserialize<MonthlyConfig>(configJson, JsonOptions);

        if (config == null)
        {
            result.AddError("Invalid Monthly configuration.");
            return;
        }

        if (!config.IsLastDayOfMonth && (config.DatesOfMonth == null || config.DatesOfMonth.Count == 0))
        {
            result.AddError("At least one date of the month must be selected for monthly schedule.");
            return;
        }

        if (config.IsLastDayOfMonth && config.DatesOfMonth != null && config.DatesOfMonth.Count > 0)
            result.AddWarning("Both 'Last Day' and specific dates are selected. 'Last Day' will take precedence.");

        if (config.DatesOfMonth != null)
        {
            var invalidDates = config.DatesOfMonth.Where(d => d < 1 || d > 31).ToList();
            if (invalidDates.Any())
                result.AddError($"Invalid dates: {string.Join(", ", invalidDates)}. Must be between 1 and 31.");

            var duplicates = config.DatesOfMonth.GroupBy(d => d).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Any())
                result.AddWarning($"Duplicate dates detected: {string.Join(", ", duplicates)}. These will be removed.");

            if (config.DatesOfMonth.Count > 20)
                result.AddWarning("More than 20 dates selected in a month. This may result in frequent releases.");
        }
    }

    private void ValidateQuarterlyConfig(string configJson, ValidationResult result)
    {
        var config = JsonSerializer.Deserialize<QuarterlyConfig>(configJson, JsonOptions);

        if (config == null)
        {
            result.AddError("Invalid Quarterly configuration.");
            return;
        }

        if (config.Months == null || config.Months.Count == 0)
        {
            result.AddError("At least one month must be selected for quarterly schedule.");
            return;
        }

        if (config.Months.Any(m => m < 1 || m > 12))
        {
            result.AddError("Months must contain values between 1 (January) and 12 (December).");
            return;
        }

        if (config.DaysOfMonth == null || config.DaysOfMonth.Count == 0)
        {
            result.AddError("At least one day must be selected in DaysOfMonth for quarterly schedule.");
            return;
        }

        // Validate each day in DaysOfMonth (1-31)
        if (config.DaysOfMonth.Any(d => d < 1 || d > 31))
        {
            result.AddError($"DaysOfMonth must contain values between 1 and 31. Invalid values found: {string.Join(", ", config.DaysOfMonth.Where(d => d < 1 || d > 31))}");
            return;
        }

        var invalidMonths = config.Months.Where(m => m < 1 || m > 12).ToList();
        if (invalidMonths.Any())
            result.AddError($"Invalid months: {string.Join(", ", invalidMonths)}. Must be between 1 (January) and 12 (December).");

        var duplicates = config.Months.GroupBy(m => m).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Any())
            result.AddWarning($"Duplicate months detected: {string.Join(", ", duplicates)}. These will be removed.");

        if (config.Months.Count == 4)
        {
            var sorted = config.Months.Where(m => m >= 1 && m <= 12).OrderBy(m => m).ToList();
            var isStandardQuarter = false;

            isStandardQuarter =
                (sorted[0] == 1 && sorted[1] == 4 && sorted[2] == 7 && sorted[3] == 10) ||
                (sorted[0] == 2 && sorted[1] == 5 && sorted[2] == 8 && sorted[3] == 11) ||
                (sorted[0] == 3 && sorted[1] == 6 && sorted[2] == 9 && sorted[3] == 12);

            if (!isStandardQuarter)
                result.AddWarning("This quarterly pattern is not standard. Ensure it's intentional.");
        }
        else
        {
            result.AddWarning($"You selected {config.Months.Count} months. Typical quarterly schedules have 4 months.");
        }
    }

    private void ValidateYearlyConfig(string configJson, ValidationResult result)
    {
        var config = JsonSerializer.Deserialize<YearlyConfig>(configJson, JsonOptions);

        if (config == null)
        {
            result.AddError("Invalid Yearly configuration.");
            return;
        }

        // 1. Validate Months
        if (config.Months == null || config.Months.Count == 0)
        {
            result.AddError("At least one month must be selected for yearly schedule.");
            return;
        }

        if (config.Months.Any(m => m < 1 || m > 12))
        {
            result.AddError("Months must contain values between 1 (January) and 12 (December).");
            return;
        }

        // 2. Validate DaysOfMonth
        if (config.DaysOfMonth == null || config.DaysOfMonth.Count == 0)
        {
            result.AddError("At least one day must be selected in DaysOfMonth for yearly schedule.");
            return;
        }

        if (config.DaysOfMonth.Any(d => d < 1 || d > 31))
        {
            result.AddError($"DaysOfMonth must contain values between 1 and 31. Invalid values found: {string.Join(", ", config.DaysOfMonth.Where(d => d < 1 || d > 31))}");
            return;
        }

        // 3. Validate Time is provided
        if (string.IsNullOrEmpty(config.Time))
        {
            result.AddError("Time is required for Yearly frequency.");
            return;
        }

        // 4. Validate Time format
        if (!IsValidTimeFormat(config.Time))
        {
            result.AddError("Time must be in HH:mm format (e.g., 09:30, 14:45) with hours 00-23 and minutes 00-59.");
            return;
        }

        // 5. Optional: Warning if too many months selected
        if (config.Months.Count > 6)
        {
            result.AddWarning($"You selected {config.Months.Count} months. Consider using Monthly or Quarterly frequency instead.");
        }

        // 6. Optional: Warning if too many days selected
        if (config.DaysOfMonth.Count > 15)
        {
            result.AddWarning($"You selected {config.DaysOfMonth.Count} days. Consider using Daily or Custom frequency instead.");
        }

        // 7. Sort for consistency
        config.Months = config.Months.OrderBy(m => m).ToList();
        config.DaysOfMonth = config.DaysOfMonth.OrderBy(d => d).ToList();
    }
    private void ValidateCustomConfig(string configJson, ValidationResult result)
    {
        var config = JsonSerializer.Deserialize<CustomConfig>(configJson, JsonOptions);

        if (config == null)
        {
            result.AddError("Invalid Custom configuration.");
            return;
        }

        if (config.IntervalDays <= 0)
            result.AddError("Interval days must be greater than zero.");

        if (config.IntervalDays < 1)
            result.AddError("Interval days must be at least 1 day.");

        if (config.IntervalDays > 365)
            result.AddWarning($"Interval of {config.IntervalDays} days is over 1 year. Is this intentional?");

        if (config.IntervalDays < 7 && config.IntervalDays != 1)
            result.AddWarning($"Interval of {config.IntervalDays} days is less than a week. Consider using Daily or Weekly frequency.");

        if (config.IntervalDays == 7)
            result.AddWarning("7 days interval is essentially weekly. Consider using Weekly frequency.");
    }

    private bool IsValidTimeFormat(string time)
    {
        if (string.IsNullOrEmpty(time))
            return false;
        
        if (!TimeSpan.TryParse(time, out var parsedTime))
            return false;
        
        return parsedTime.TotalHours >= 0 && parsedTime.TotalHours < 24;
    }

    #endregion
}