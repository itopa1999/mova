using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;

namespace Mova.Domain.Entities;

[Table("banks")]
public class Bank : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Ussd { get; set; }
    public string? Logo { get; set; }
    public bool IsActive { get; set; } = true;
}