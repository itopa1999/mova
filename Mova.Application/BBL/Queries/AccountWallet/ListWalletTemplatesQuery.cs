using System.Net;
using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Shared.Common;
using Mova.Shared.Constants;

namespace Mova.Application.BBL.Queries.WalletTemplates;

public sealed class ListWalletTemplatesQuery
{
    public sealed class Query : IRequest<BaseResult<ListWalletTemplatesResponseDto>>
    {
        [Range(1, 1000000)]
        public int Page { get; set; } = 1;

        [Range(1, 100)]
        public int PageSize { get; set; } = 20;

        [MaxLength(100)]
        public string? Search { get; set; }

        [Range(1, long.MaxValue)]
        public long? CategoryId { get; set; }

        [EnumDataType(typeof(WalletType))]
        public WalletType? WalletType { get; set; }
    }

    public sealed class ListWalletTemplatesResponseDto
        : BasePaginationResponse<WalletTemplateDto>
    {
    }

    public sealed class WalletTemplateDto
    {
        public long Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public long CategoryId { get; init; }
        public string CategoryName { get; init; } = string.Empty;
        public string CategoryIcon { get; init; } = string.Empty;
        public string WalletType { get; init; } = string.Empty;
        public decimal DefaultTargetAmount { get; init; }
        public decimal DefaultReleaseAmount { get; init; }
        public string DefaultFrequency { get; init; } = string.Empty;
        public string DefaultFrequencyConfig { get; init; } = string.Empty;
        public string DefaultPayoutDestination { get; init; } = string.Empty;
        public string IconName { get; init; } = string.Empty;
        public List<string> Tags { get; init; } = new();
        public int SortOrder { get; init; }
        public int UsageCount { get; init; }
    }

    public sealed class Handler
        : IRequestHandler<Query, BaseResult<ListWalletTemplatesResponseDto>>
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICacheService _cacheService;

        public Handler(
            IUnitOfWork unitOfWork,
            ICacheService cacheService)
        {
            _unitOfWork = unitOfWork;
            _cacheService = cacheService;
        }

        public async Task<BaseResult<ListWalletTemplatesResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var page = Math.Clamp(request.Page, 1, 1_000_000);
            var pageSize = Math.Clamp(request.PageSize, 1, 100);

            var categoryId = request.CategoryId is > 0
                ? request.CategoryId
                : null;

            var search = string.IsNullOrWhiteSpace(request.Search)
                ? null
                : request.Search.Trim().ToLowerInvariant();

            var walletType = request.WalletType;

            var cacheKey = CacheKeys.WalletTemplates(
                page,
                pageSize,
                search,
                categoryId,
                walletType?.ToString());

            var response = await _cacheService.GetOrSetAsync(
                cacheKey,
                ct => LoadTemplatesFromDbAsync(
                    page, pageSize, search, categoryId, walletType, ct),
                timeout: CacheDuration,
                cancellationToken: cancellationToken);

            return new BaseResult<ListWalletTemplatesResponseDto>(
                HttpStatusCode.OK,
                "Wallet templates retrieved successfully.",
                response);
        }

        private async Task<ListWalletTemplatesResponseDto?> LoadTemplatesFromDbAsync(
            int page,
            int pageSize,
            string? search,
            long? categoryId,
            WalletType? walletType,
            CancellationToken cancellationToken)
        {
            var query = _unitOfWork.Query<WalletTemplate>()
                .AsNoTracking()
                .Where(t => t.IsActive);

            if (walletType is { } wt)
            {
                query = wt == WalletType.Scheduled
                    ? query.Where(t => t.WalletType == WalletType.Scheduled
                                    || t.WalletType == null)
                    : query.Where(t => t.WalletType == wt);
            }

            if (categoryId.HasValue)
            {
                query = query.Where(t => t.CategoryId == categoryId.Value);
            }

            if (search is not null)
            {
                var term = search;

                query = query.Where(t =>
                    t.Name.ToLower().Contains(term) ||
                    t.Description.ToLower().Contains(term) ||
                    t.Tags.Any(tag => tag.ToLower().Contains(term)));
            }

            var totalCount = await query.CountAsync(cancellationToken);

            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            var templates = await query
                .Include(t => t.Category)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            var items = templates.Select(t => new WalletTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                CategoryId = t.CategoryId,
                CategoryName = t.Category?.Name ?? "Other",
                CategoryIcon = t.Category?.Icon ?? "FileText",
                WalletType = t.WalletType.HasValue
                    ? t.WalletType.Value.ToString().ToLowerInvariant()
                    : WalletType.Scheduled.ToString().ToLowerInvariant(),
                DefaultTargetAmount = t.DefaultTargetAmount.ToDecimal(),
                DefaultReleaseAmount = t.DefaultReleaseAmount.ToDecimal(),
                DefaultFrequency = t.DefaultFrequency.ToString(),
                DefaultFrequencyConfig = t.DefaultFrequencyConfig,
                DefaultPayoutDestination =
                    t.DefaultPayoutDestination.ToString().ToLowerInvariant(),
                IconName = t.IconName,
                Tags = t.Tags.ToList(),
                SortOrder = t.SortOrder,
                UsageCount = t.UsageCount
            }).ToList();

            return new ListWalletTemplatesResponseDto
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Items = items
            };
        }
    }
}

// todo cache need invladidation