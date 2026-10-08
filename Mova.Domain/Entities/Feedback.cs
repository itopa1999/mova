using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;

namespace Mova.Domain.Entities;

[Table("feedbacks")]
public class Feedback : BaseEntity
{
    public string UserPublicId { get; set; } = string.Empty;

    [Range(0, 5)]
    public int Rating { get; set; }

    [MaxLength(16)]
    public string? Experience { get; set; }

    [MaxLength(256)]
    public string? Improvements { get; set; }

    [MaxLength(600)]
    public string? Message { get; set; }

    public bool IsDone { get; set; } = false;
}