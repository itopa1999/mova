
namespace Mova.Domain.ValueObjects;

public class ValidationResult
{
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public bool IsValid => Errors.Count == 0;

    public void AddError(string error) => Errors.Add(error);
    public void AddWarning(string warning) => Warnings.Add(warning);

    public void AddErrors(IEnumerable<string> errors) => Errors.AddRange(errors);
    public void AddWarnings(IEnumerable<string> warnings) => Warnings.AddRange(warnings);

    public string GetErrorMessage() => string.Join("\n", Errors);
    public string GetWarningMessage() => string.Join("\n", Warnings);

    public override string ToString()
    {
        var messages = new List<string>();
        if (Errors.Any()) messages.Add($"Errors: {GetErrorMessage()}");
        if (Warnings.Any()) messages.Add($"Warnings: {GetWarningMessage()}");
        return messages.Any() ? string.Join(" | ", messages) : "✅ Valid";
    }
}