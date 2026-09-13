using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;

namespace Mova.Domain.Entities;

[Table("notifications")]
public class AppNotification : BaseEntity
{
    public string UserPublicId { get; set; } = string.Empty;

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public string? ActionUrl { get; set; }

    public string? Metadata { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}