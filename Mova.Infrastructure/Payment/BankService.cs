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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(1);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IExternalApiClient _externalApiClient;
    private readonly ExternalApiSettings _externalApiSettings;
    private readonly ICacheService _cacheService;

    public BankService(
        IUnitOfWork unitOfWork,
        IExternalApiClient externalApiClient,
        IOptions<ExternalApiSettings> externalApiSettings,
        ICacheService cacheService)
    {
        _unitOfWork = unitOfWork;
        _externalApiClient = externalApiClient;
        _externalApiSettings = externalApiSettings.Value;
        _cacheService = cacheService;
    }

    public async Task<List<BankDto>> GetAllBanksAsync(string? name = null)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name)
            ? null
            : name.Trim();

        var cacheKey = normalizedName is null
            ? CacheKeys.BanksAll()
            : CacheKeys.BanksSearch(normalizedName);
        return await _cacheService.GetOrSetAsync(
            cacheKey,
            async cancellationToken =>
                await GetBanksFromDbAsync(
                    normalizedName,
                    cancellationToken),
            timeout: CacheDuration,
            cancellationToken: CancellationToken.None)
            ?? new List<BankDto>();
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
            var term = name.Trim().ToLowerInvariant();

            query = query.Where(b =>
                b.Name.ToLower().Contains(term));
        }

        var banks = await query
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);

        return banks.Select(MapToDto).ToList();
    }

    public async Task<BankDto?> GetBankByCodeAsync(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var cacheKey = CacheKeys.BankByCode(code);

        return await _cacheService.GetOrSetAsync<BankDto>(
            cacheKey,
            async cancellationToken =>
                await GetBankByCodeFromDbAsync(
                    code.Trim(),
                    cancellationToken),
            timeout: CacheDuration,
            cancellationToken: CancellationToken.None);
    }

    private async Task<BankDto?> GetBankByCodeFromDbAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var normalizedCode = code.Trim().ToLowerInvariant();

        var bank = await _unitOfWork.Query<Bank>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.Code.ToLower() == normalizedCode && b.IsActive,
                cancellationToken);

        return bank is null ? null : MapToDto(bank);
    }

    public async Task<BankDto?> GetBankBySlugAsync(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var cacheKey = CacheKeys.BankBySlug(slug);

        return await _cacheService.GetOrSetAsync<BankDto>(
            cacheKey,
            async cancellationToken =>
                await GetBankBySlugFromDbAsync(
                    slug.Trim(),
                    cancellationToken),
            timeout: CacheDuration,
            cancellationToken: CancellationToken.None);
    }

    private async Task<BankDto?> GetBankBySlugFromDbAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();

        var bank = await _unitOfWork.Query<Bank>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.Slug.ToLower() == normalizedSlug && b.IsActive,
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

        var existingBanksByCode = existingBanks
            .GroupBy(b => b.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var bankCodes = banks
            .Select(bank => bank.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existingBank in existingBanks)
        {
            if (!bankCodes.Contains(existingBank.Code))
            {
                existingBank.IsActive = false;
                _unitOfWork.Update(existingBank);
            }
        }

        foreach (var bankData in banks)
        {
            if (string.IsNullOrWhiteSpace(bankData.Code))
                continue;

            if (!existingBanksByCode.TryGetValue(
                    bankData.Code,
                    out var existingBank))
            {
                var newBank = new Bank
                {
                    Name = bankData.Name,
                    Slug = bankData.Slug,
                    Code = bankData.Code,
                    Ussd = bankData.Ussd,
                    Logo = bankData.Logo,
                    IsActive = true
                };

                await _unitOfWork.AddAsync(
                    newBank,
                    cancellationToken);

                existingBanksByCode[bankData.Code] = newBank;
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

        await _cacheService.DeletePrefixAsync(
            CacheKeys.BanksPrefix,
            cancellationToken);
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