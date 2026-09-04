using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Mova.Domain.Common;
using Mova.Domain.Enums;

namespace Mova.Domain.Entities;

[Table("virtual_accounts")]
public class VirtualAccount : BaseEntity
{
    [MaxLength(100)]
    public string UserPublicId { get; set; } = string.Empty;

    [MaxLength(50)]
    public PaymentProvider Provider { get; set; } = PaymentProvider.Paystack;

    [MaxLength(150)]
    public string? ProviderCustomerId { get; set; }

    [MaxLength(150)]
    public string? ProviderAccountId { get; set; }

    [Required]
    [MaxLength(20)]
    public string AccountNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string BankName { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string AccountName { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Currency { get; set; } = "NGN";

    [MaxLength(30)]
    public VirtualAccountStatus Status { get; set; } = VirtualAccountStatus.Active;
}