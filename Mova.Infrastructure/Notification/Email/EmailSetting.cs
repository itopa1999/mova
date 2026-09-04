namespace Mova.Infrastructure.Notification;

public sealed class EmailSettings
{
    public const string SectionName = "Email";

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FromEmail { get; init; } = string.Empty;

    public string FromName { get; init; } = string.Empty;

    public bool UseTls { get; init; }

    public bool UseSsl { get; init; }
}