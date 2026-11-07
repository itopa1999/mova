namespace Mova.Application.Helpers;

public sealed class TrusteeOptions
{
    public const string SectionName = "Trustee";

    public int InvitationExpiryHours { get; set; } = 24;
    public int ReleaseRequestExpiryHours { get; set; } = 3; 
}