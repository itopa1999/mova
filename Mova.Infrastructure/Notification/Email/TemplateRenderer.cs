namespace Mova.Infrastructure.Notification.Email;

public sealed class TemplateRenderer
{
    public async Task<string> RenderAsync(
        string templateName,
        Dictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        var templatePath = Path.Combine(
            AppContext.BaseDirectory,
            "Notification",
            "Email",
            "Templates",
            templateName);

        if (!File.Exists(templatePath))
            throw new FileNotFoundException(
                $"Template '{templateName}' not found.",
                templatePath);

        var html = await File.ReadAllTextAsync(templatePath, cancellationToken);

        foreach (var value in values)
        {
            html = html.Replace(
                $"{{{{{value.Key}}}}}",
                value.Value);
        }

        return html;
    }
}
