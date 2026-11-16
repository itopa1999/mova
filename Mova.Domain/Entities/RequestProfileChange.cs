using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;

namespace Mova.Domain.Entities;

[Table("request_profile_changes")]
public class RequestProfileChange : BaseEntity
{
    public string UserPublicId { get; set; } = null!;

    public string? NewFirstName { get; set; }
    public string? NewOtherName  { get; set; }
    public string? NewLastName  { get; set; }
    public string? NewEmail     { get; set; }
    public string? NewPhoneNumber { get; set; }

    public string RequestReason { get; set; } = null!;
    public string? RequestComment { get; set; }


    public string? OldFirstName { get; set; }
    public string? OldOtherName  { get; set; }
    public string? OldLastName  { get; set; }
    public string? OldEmail     { get; set; }
    public string? OldPhoneNumber { get; set; }

    public bool IsApproved { get; set; }
    public DateTimeOffset?  ApprovedAt { get; set; }

    public bool IsRejected { get; set; }
    public string? RejectionReason { get; set; }
    public string? RejectedBy { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }

}