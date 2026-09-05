namespace Mova.Infrastructure.Payment.Flutterwave;

public sealed class FlutterwaveSettings
{
    public const string SectionName = "Flutterwave";

    public string SecretKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty; 
}
