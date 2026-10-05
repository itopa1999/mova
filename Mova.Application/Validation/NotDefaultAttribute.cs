using System.ComponentModel.DataAnnotations;

namespace Mova.Application.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotDefaultAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        return value switch
        {
            DateTime dateTime => dateTime != default,
            DateTimeOffset dateTimeOffset => dateTimeOffset != default,
            Guid guid => guid != Guid.Empty,
            _ => true
        };
    }
}
