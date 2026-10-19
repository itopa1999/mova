
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Constants;

namespace Mova.Application.BBL.Queries.AccountWallet;

public sealed class GetWalletCategories
{
    public sealed class Query : IRequest<BaseResult<List<WalletCategoryDto>>>
    {
    }

    public sealed class WalletCategoryDto
    {
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Icon { get; init; } = string.Empty;
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
                var categories = await _cacheService.GetOrSetAsync(
                    CacheKeys.WalletCategories(),
                    LoadCategoriesFromDbAsync,
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
            CancellationToken cancellationToken)
        {
            return await _unitOfWork
                .Query<WalletCategory>()
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => new WalletCategoryDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    Icon = x.Icon ?? string.Empty,
                })
                .ToListAsync(cancellationToken);
        }
    }
}


// invalidation cache key later