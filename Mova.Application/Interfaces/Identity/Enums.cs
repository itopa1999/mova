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
    Money Balance
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