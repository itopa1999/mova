using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;

namespace Mova.Domain.Entities;

[Table("otp_verifications")]
public class OtpVerification : BaseEntity
{
    public string UserPublicId { get; set; }
    public string OtpCode { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public bool IsUsed { get; set; }
    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}