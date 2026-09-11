namespace Mova.Application.Interfaces.Payment;

public class BankDto
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Ussd { get; set; }
    public string? Logo { get; set; }
}

public sealed class ResolveBankAccountResponse
{
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
}


public sealed class PaymentInitializationResultDto
{
    public bool Success { get; set; }

    public string? Message { get; set; }

    public string? AuthorizationUrl { get; set; }
}
