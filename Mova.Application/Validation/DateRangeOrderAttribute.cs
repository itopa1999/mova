using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Mova.Application.Validation;

[AttributeUsage(AttributeTargets.Class)]
public sealed class DateRangeOrderAttribute(
    string startProperty,
    string endProperty) : ValidationAttribute
{
    protected override ValidationResult? IsValid(
        object? value,
        ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        var type = value.GetType();
        var start = type.GetProperty(startProperty, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(value) as DateTimeOffset?;
        var end = type.GetProperty(endProperty, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(value) as DateTimeOffset?;

        return start.HasValue && end.HasValue && start > end
            ? new ValidationResult(
                ErrorMessage ?? "The start date must not be after the end date.",
                [startProperty, endProperty])
            : ValidationResult.Success;
    }
}
