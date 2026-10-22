
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Constants;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetWalletCategories
{
    public sealed class Query : IRequest<BaseResult<List<WalletCategoryDto>>>
    {
        public WalletType? WalletType { get; init; }
    }

    public sealed class WalletCategoryDto
    {
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Icon { get; init; } = string.Empty;
        public string WalletType { get; init; } = string.Empty;
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<List<WalletCategoryDto>>>
    {
        private static readonly TimeSpan CacheDuration =
            TimeSpan.FromDays(1);

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICacheService _cacheService;

        public Handler(
            IUnitOfWork unitOfWork,
            ICacheService cacheService)
        {
            _unitOfWork = unitOfWork;
            _cacheService = cacheService;
        }

        public async Task<BaseResult<List<WalletCategoryDto>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            try
            {
                var cacheKey = CacheKeys.WalletCategories(
                    request.WalletType?.ToString() ?? "All");

                var categories = await _cacheService.GetOrSetAsync(
                    cacheKey,
                    ct => LoadCategoriesFromDbAsync(
                        request.WalletType,
                        ct),
                    timeout: CacheDuration,
                    cancellationToken: cancellationToken);

                return new BaseResult<List<WalletCategoryDto>>(
                    HttpStatusCode.OK,
                    "Wallet categories retrieved successfully.",
                    categories ?? []);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new BaseResult<List<WalletCategoryDto>>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving wallet categories. Please try again later.");
            }
        }

        private async Task<List<WalletCategoryDto>> LoadCategoriesFromDbAsync(
            WalletType? walletType,
            CancellationToken cancellationToken)
        {
            var query = _unitOfWork
                .Query<WalletCategory>()
                .AsNoTracking();

            if (walletType.HasValue)
            {
                query = query.Where(
                    x => x.WalletType == walletType.Value);
            }

            return await query
                .OrderBy(x => x.Id)
                .Select(x => new WalletCategoryDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Icon = x.Icon ?? string.Empty,
                    WalletType = x.WalletType.ToString()
                })
                .ToListAsync(cancellationToken);
        }
    }
}

// invalidation cache key later