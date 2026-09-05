namespace Mova.Infrastructure.Payment.Paystack;

public sealed class PaystackSettings
{
    public const string SectionName = "Paystack";

    public string SecretKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}