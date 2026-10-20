namespace Mova.Application.Interfaces.Payment;

public class BankDto
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Ussd { get; set; }
    public string? Logo { get; set; }
}

public sealed class ResolveBankAccountResult
{
    public bool CallSucceeded { get; set; }
    public string? Message { get; set; }

    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int? BankId { get; set; }
}


public sealed class PaymentInitializationResultDto
{
    public bool CallSucceeded  { get; set; }
    public string? Message { get; set; }

    public string? AuthorizationUrl { get; set; }
    public string? TransactionReference { get; set; }
    public string? PaymentReference { get; set; }
    public string? AccessCode { get; set; }
    public string? Reference { get; set; }
}
