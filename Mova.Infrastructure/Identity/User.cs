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

    public string? ProfilePicture { get; set; } = string.Empty;
            
    public string? TransactionPinHash { get; set; }

    public DateTimeOffset? TransactionPinSetAt { get; set; }

    public DateTimeOffset? TransactionPinResetAt { get; set; }

    public DateTimeOffset? TransactionPinChangedAt { get; set; }

    [MaxLength(200)]
    public string? LastKnownDeviceId { get; set; }
    
    // Alerts when the account is accessed from a new device or location.
    public bool NotifyLoginAlerts { get; set; } = true;

    // Alerts when a controlled wallet releases money or a payout lands.
    public bool NotifyReleaseAlerts { get; set; } = true;

    // Product updates — new features, improvements, announcements.
    public bool NotifyProductUpdates { get; set; } = true;

    // Tips and promotional content. Default OFF — opt-in only.
    public bool NotifyPromotions { get; set; } = false;

    public Money Balance { get; set; } = Money.FromNaira(0); // The current balance of the user, representing the total amount of funds available for transactions
}