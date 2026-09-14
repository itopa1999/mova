using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;

namespace Mova.Domain.Entities;

[Table("feature_flags")]
public class FeatureFlag : BaseEntity
{
    public FeatureFlagName Name { get; set; } = FeatureFlagName.AllowWithdrawFunds;

    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public string? Metadata { get; set; }

}