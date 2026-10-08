namespace Mova.Api.Configurations;

public sealed class HangfireSettings
{
    public const string SectionName = "Hangfire";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}