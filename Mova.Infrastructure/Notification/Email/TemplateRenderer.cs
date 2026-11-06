using System.Net;
using System.Text.RegularExpressions;
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
            var replacement = value.Key.Equals("Message", StringComparison.OrdinalIgnoreCase)
                ? ConvertMarkdownToHtml(value.Value)
                : value.Value;

            html = Regex.Replace(
                html,
                $"{{\\s*{Regex.Escape(value.Key)}\\s*}}",
                _ => replacement,
                RegexOptions.IgnoreCase);
        }

        return html;
    }

    private static string ConvertMarkdownToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var normalized = markdown
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        if (LooksLikeHtml(normalized))
            return normalized;

        var lines = normalized.Split('\n');
        var blocks = new List<string>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index].Trim();

            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            if (TryParseHeading(line, out var level, out var headingText))
            {
                var safeLevel = Math.Min(level, 3);
                blocks.Add($"<h{safeLevel}>{FormatInlineHtml(headingText)}</h{safeLevel}>");
                index++;
                continue;
            }

            if (IsListItem(line))
            {
                var items = new List<string>();
                while (index < lines.Length)
                {
                    var current = lines[index].Trim();

                    if (string.IsNullOrWhiteSpace(current))
                    {
                        index++;
                        break;
                    }

                    if (!IsListItem(current))
                        break;

                    var itemText = current.TrimStart('-', '*', '•').Trim();
                    items.Add($"<li>{FormatInlineHtml(itemText)}</li>");
                    index++;
                }

                blocks.Add("<ul>" + string.Concat(items) + "</ul>");
                continue;
            }

            var paragraphLines = new List<string>();
            while (index < lines.Length)
            {
                var current = lines[index].Trim();

                if (string.IsNullOrWhiteSpace(current)
                    || TryParseHeading(current, out _, out _)
                    || IsListItem(current))
                {
                    break;
                }

                paragraphLines.Add(current);
                index++;
            }

            if (paragraphLines.Count > 0)
            {
                blocks.Add($"<p>{FormatInlineHtml(string.Join(" ", paragraphLines))}</p>");
                continue;
            }

            index++;
        }

        return string.Join(Environment.NewLine, blocks);
    }

    private static bool LooksLikeHtml(string content)
    {
        return Regex.IsMatch(
            content,
            @"<\s*(p|h[1-6]|ul|ol|li|strong|b|em|i|a|div|span)\b",
            RegexOptions.IgnoreCase);
    }

    private static bool TryParseHeading(string line, out int level, out string headingText)
    {
        var match = Regex.Match(line, "^(#{1,6})\\s+(.*)$");

        if (!match.Success)
        {
            level = 0;
            headingText = string.Empty;
            return false;
        }

        level = match.Groups[1].Value.Length;
        headingText = match.Groups[2].Value.Trim();
        return true;
    }

    private static bool IsListItem(string line)
    {
        return Regex.IsMatch(line, "^(?:[-*]|[•])\\s+.+$");
    }

    private static string FormatInlineHtml(string text)
    {
        var formatted = WebUtility.HtmlEncode(text);

        formatted = Regex.Replace(
            formatted,
            @"\[([^\]]+)\]\((https?://[^)]+)\)",
            match =>
            {
                var label = WebUtility.HtmlEncode(match.Groups[1].Value);
                var href = WebUtility.HtmlEncode(match.Groups[2].Value);
                return $"<a href=\"{href}\" target=\"_blank\" rel=\"noopener noreferrer\">{label}</a>";
            });

        formatted = Regex.Replace(
            formatted,
            @"(?<![""'=])https?://[^\s<>""']+",
            match =>
            {
                var href = WebUtility.HtmlEncode(match.Value);
                var label = match.Value;
                return $"<a href=\"{href}\" target=\"_blank\" rel=\"noopener noreferrer\">{label}</a>";
            });

        formatted = Regex.Replace(
            formatted,
            @"\*\*(.+?)\*\*",
            m => $"<strong>{m.Groups[1].Value}</strong>");

        formatted = Regex.Replace(
            formatted,
            @"__(.+?)__",
            m => $"<strong>{m.Groups[1].Value}</strong>");

        formatted = Regex.Replace(
            formatted,
            @"(?<!\*)\*(.+?)(?<!\*)\*(?!\*)",
            m => $"<em>{m.Groups[1].Value}</em>");

        formatted = Regex.Replace(
            formatted,
            @"(?<!_)_(.+?)(?<!_)_(?!_)",
            m => $"<em>{m.Groups[1].Value}</em>");

        return formatted;
    }
}
