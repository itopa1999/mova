using Mova.Domain.ValueObjects;

namespace Mova.Application.Interfaces.Identity;

public sealed record UserIdentityDto(
    long Id,
    string PublicId,
    string FirstName,
    string? OtherNames,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string? ProfilePicture,
    Money Balance,
    string TransactionPinHash,
    bool NotifyLoginAlerts,
    bool NotifyReleaseAlerts,
    bool NotifyProductUpdates,
    bool NotifyPromotions,
    string LastKnownDeviceId
)
{
    public string FullName =>
        string.Join(
            " ",
            new[]
            {
                FirstName,
                OtherNames,
                LastName
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
}