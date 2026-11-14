namespace Mova.Application.Interfaces.Admin;

public class AdminUserListDto
{
    public long Id { get; set; }
    public string PublicId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OnboardingStage { get; set; } = string.Empty;
    public DateTimeOffset? DateJoined { get; set; }
}