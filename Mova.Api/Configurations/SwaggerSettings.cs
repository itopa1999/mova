namespace Mova.Api.Configurations;

public class SwaggerSettings
{
    public const string SectionName = "SwaggerCredentials";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}