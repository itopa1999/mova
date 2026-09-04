using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Mova.Domain.ValueObjects;

namespace Mova.Infrastructure.Identity;

[Table("users")]
public class User : IdentityUser<long>
{
    [MaxLength(100)]
    public string PublicId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? OtherNames { get; set; }

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    public string FullName => string.Join(
        " ",
        new[] { FirstName, OtherNames, LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
            
    public string? TransactionPinHash { get; set; }
    public Money Balance { get; set; } = Money.FromNaira(0); // The current balance of the user, representing the total amount of funds available for transactions
}