namespace Mova.Application.Helpers;

public sealed class TrusteeOptions
{
    public const string SectionName = "Trustee";

    public string ConsentBaseUrl { get; set; } = string.Empty;

    public int InvitationExpiryHours { get; set; } = 24;
}