namespace Mova.Shared.Common;
public sealed class UtilityConfig
{
    public string UtilityType { get; set; } = "";

    // Airtime + Data
    public string? Network { get; set; }
    public string? PhoneNumber { get; set; }
    public string? PlanCode { get; set; }

    // Cable
    public string? CableProvider { get; set; }
    public string? SmartcardNumber { get; set; }
    public string? PackageCode { get; set; }

    // Electricity
    public string? Disco { get; set; }
    public string? MeterNumber { get; set; }
    public string? MeterType { get; set; }
}