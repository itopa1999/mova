using System.Text.Json;
using Mova.Application.Interfaces.Service;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Services;

public sealed class SavingsCircleService : ISavingsCircleService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

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
                var hasLastDay = config.IsLastDayOfMonth == true;

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


    

    private sealed class FrequencyConfigDto
    {
        public string? Type { get; set; }
        public List<int>? DaysOfWeek { get; set; }
        public List<int>? DatesOfMonth { get; set; }
        public bool? IsLastDayOfMonth { get; set; }
    }
}