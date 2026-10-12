using System.ComponentModel.DataAnnotations;
using Mova.Domain.Enums;

namespace Mova.Api.Controllers.V1;

public sealed class EncryptedPinRequest
{
    [Required]
    [MaxLength(684)]
    public string Pin { get; init; } = string.Empty;
    
    [Required]
    public PinChallengeType PinChallengeType { get; init; }
}

public sealed class ChangeEncryptedPinRequest
{
    [Required]
    [MaxLength(684)]
    public string CurrentPin { get; init; } = string.Empty;

    [Required]
    [MaxLength(684)]
    public string NewPin { get; init; } = string.Empty;
}
