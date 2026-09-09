

using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;

namespace Mova.Domain.Entities;


[Table("wallet_categories")]
public class WalletCategory : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
}