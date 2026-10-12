using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;

namespace Mova.Domain.Entities;

[Table("pin_challenges")]
public class PinChallenge : BaseEntity
{
    public required string UserPublicId { get; set; }
    public PinChallengeType PinChallengeType { get; set; }
    public bool IsUsed { get; set; }
    public DateTimeOffset UsedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(2);
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}