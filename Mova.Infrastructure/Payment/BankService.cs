using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.ExternalAPI;
using Mova.Application.Interfaces.Payment;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Infrastructure.ExternalAPI;
using Mova.Shared.Constants;

namespace Mova.Infrastructure.Payment;

public sealed class BankService : IBankService
{
    private static readonly TimeSpan BankCacheTtl = TimeSpan.FromHours(6);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IExternalApiClient _externalApiClient;
    private readonly ExternalApiSettings _externalApiSettings;
    private readonly ICacheService _cache;

    public BankService(
        IUnitOfWork unitOfWork,
        IExternalApiClient externalApiClient,
        IOptions<ExternalApiSettings> externalApiSettings,
        ICacheService cache)
    {
        _unitOfWork = unitOfWork;
        _externalApiClient = externalApiClient;
        _externalApiSettings = externalApiSettings.Value;
        _cache = cache;
    }

    public async Task<List<BankDto>> GetAllBanksAsync(string? name = null)
    {
        var hasFilter = !string.IsNullOrWhiteSpace(name);

        var cacheKey = hasFilter
            ? CacheKeys.BanksSearch(name!)
            : CacheKeys.BanksAll();

        var banks = await _cache.GetOrSetFastAsync(
            cacheKey,
            ct => GetBanksFromDbAsync(hasFilter ? name : null, ct),
            timeout: BankCacheTtl);

        return banks ?? new List<BankDto>();
    }

    private async Task<List<BankDto>> GetBanksFromDbAsync(
        string? name,
        CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Query<Bank>()
            .AsNoTracking()
            .Where(b => b.IsActive);

        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(b =>
                EF.Functions.ILike(b.Name, $"%{name.Trim()}%"));
        }

        var banks = await query
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        return banks.Select(MapToDto).ToList();
    }

    public async Task<BankDto?> GetBankByCodeAsync(string code)
    {
        return await _cache.GetOrSetFastAsync(
            CacheKeys.BankByCode(code),
            ct => GetBankByCodeFromDbAsync(code, ct),
            timeout: BankCacheTtl);
    }

    private async Task<BankDto?> GetBankByCodeFromDbAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var bank = await _unitOfWork.Query<Bank>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.Code == code && b.IsActive,
                cancellationToken);

        return bank is null ? null : MapToDto(bank);
    }

    public async Task<BankDto?> GetBankBySlugAsync(string slug)
    {
        return await _cache.GetOrSetFastAsync(
            CacheKeys.BankBySlug(slug),
            ct => GetBankBySlugFromDbAsync(slug, ct),
            timeout: BankCacheTtl);
    }

    private async Task<BankDto?> GetBankBySlugFromDbAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var bank = await _unitOfWork.Query<Bank>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.Slug == slug && b.IsActive,
                cancellationToken);

        return bank is null ? null : MapToDto(bank);
    }

    public async Task RefreshBanksAsync(
        CancellationToken cancellationToken = default)
    {
        var banks = await _externalApiClient.GetAsync<List<BankData>>(
            _externalApiSettings.NigerianBanksUrl,
            cancellationToken: cancellationToken);

        if (banks is null || banks.Count == 0)
            return;

        var existingBanks = await _unitOfWork.Query<Bank>()
            .ToListAsync(cancellationToken);

        var bankCodes = banks
            .Select(x => x.Code)
            .ToHashSet();

        foreach (var existingBank in existingBanks)
        {
            if (!bankCodes.Contains(existingBank.Code))
                existingBank.IsActive = false;
        }

        foreach (var bankData in banks)
        {
            var existingBank = existingBanks
                .FirstOrDefault(x => x.Code == bankData.Code);

            if (existingBank is null)
            {
                await _unitOfWork.AddAsync(
                    new Bank
                    {
                        Name = bankData.Name,
                        Slug = bankData.Slug,
                        Code = bankData.Code,
                        Ussd = bankData.Ussd,
                        Logo = bankData.Logo,
                        IsActive = true
                    },
                    cancellationToken);

                continue;
            }

            existingBank.Name = bankData.Name;
            existingBank.Slug = bankData.Slug;
            existingBank.Ussd = bankData.Ussd;
            existingBank.Logo = bankData.Logo;
            existingBank.IsActive = true;

            _unitOfWork.Update(existingBank);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // ─── Invalidate all bank caches after a successful refresh ───
        await _cache.DeletePrefixAsync(CacheKeys.BanksPrefix);
    }

    private static BankDto MapToDto(Bank bank)
    {
        return new BankDto
        {
            Name = bank.Name,
            Slug = bank.Slug,
            Code = bank.Code,
            Ussd = bank.Ussd,
            Logo = bank.Logo
        };
    }

    private sealed class BankData
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string? Ussd { get; set; }
        public string? Logo { get; set; }
    }
}