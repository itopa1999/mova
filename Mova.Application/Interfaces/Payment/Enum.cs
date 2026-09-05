namespace Mova.Application.Interfaces.Payment;

public class BankDto
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Ussd { get; set; }
    public string? Logo { get; set; }
}