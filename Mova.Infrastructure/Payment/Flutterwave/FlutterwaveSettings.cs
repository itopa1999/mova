namespace Mova.Infrastructure.Payment.Flutterwave;

public sealed class FlutterwaveSettings
{
    public const string SectionName = "Flutterwave";

    public string SecretHash { get; set; } = string.Empty;
}
