namespace Mova.Infrastructure.Notification.Sms;

public sealed class TermiiSettings
{
    public const string SectionName = "TermiiSettings";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
}