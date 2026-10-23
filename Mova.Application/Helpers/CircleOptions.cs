namespace Mova.Application.Helpers;

public sealed class CircleOptions
{
    public const string SectionName = "SavingCircle";
    public string InviteBaseUrl { get; set; } = string.Empty;
    public int MaxInvitesPerCircle { get; set; } = 500;
}