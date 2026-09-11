namespace Mova.Infrastructure.Payment.Monnify;

public sealed class MonnifySettings
{
    public const string SectionName = "Monnify";
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string ContractCode { get; set; } = string.Empty;
}