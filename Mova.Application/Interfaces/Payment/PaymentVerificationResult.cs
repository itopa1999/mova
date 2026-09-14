namespace Mova.Application.Interfaces.Payment;

public sealed class PaymentVerificationResult
{
    public bool IsSuccessful { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Found { get; set; }
}