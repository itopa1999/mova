namespace Mova.Application.Common.Models;

public sealed class EmailMessage
{
    public required string To { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    public bool IsHtml { get; init; } = true;

    public List<string> Cc { get; init; } = [];

    public List<string> Bcc { get; init; } = [];

    public List<EmailAttachment> Attachments { get; init; } = [];
}