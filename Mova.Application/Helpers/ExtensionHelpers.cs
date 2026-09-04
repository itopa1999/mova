using System.Text.RegularExpressions;

namespace Mova.Application.Helpers;

public static partial class ExtensionHelpers
{

    [GeneratedRegex(
        @"^\+234(?:70|71|80|81|90|91)\d{8}$",
        RegexOptions.Compiled)]
    private static partial Regex NigerianMobileRegex();

    public static string? Normalize(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        var value = phoneNumber.Trim();

        // Remove common formatting characters.
        value = value
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("(", "")
            .Replace(")", "");

        // 08012345678
        if (value.StartsWith("0"))
        {
            if (value.Length != 11)
                return null;

            value = "+234" + value[1..];
        }

        // 2348012345678
        else if (value.StartsWith("234"))
        {
            if (value.Length != 13)
                return null;

            value = "+" + value;
        }

        // +2348012345678
        else if (value.StartsWith("+234"))
        {
            if (value.Length != 14)
                return null;
        }

        else
        {
            return null;
        }

        return NigerianMobileRegex().IsMatch(value)
            ? value
            : null;
    }

    public static bool IsValid(string? phoneNumber)
        => Normalize(phoneNumber) is not null;
}
