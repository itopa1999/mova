namespace Mova.Infrastructure.ExternalAPI;
public sealed class ExternalApiSettings
{
    public const string SectionName = "ExternalApi";
    public string NigerianBanksUrl { get; set; } = string.Empty;
    public string PaymentCallbackUrl { get; set; } = string.Empty;
    public string FrontendBaseUrl { get; set; } = string.Empty;
    
}