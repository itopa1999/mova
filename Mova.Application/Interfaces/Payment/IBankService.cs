namespace Mova.Application.Interfaces.Payment;

public interface IBankService
{
    Task<List<BankDto>> GetAllBanksAsync();

    Task<BankDto?> GetBankByCodeAsync(string code);

    Task<BankDto?> GetBankBySlugAsync(string slug);

    Task RefreshBanksAsync(
        CancellationToken cancellationToken = default);
}