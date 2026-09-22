using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Mova.Application.Interfaces.Caching;
using Mova.Application.Interfaces.Persistence;
using Mova.Domain.Entities;
using Mova.Shared.Common;
using Mova.Shared.Constants;

namespace Mova.Application.BBL.Queries.WalletTemplates;

public sealed class ListWalletTemplatesQuery
{
    private static readonly TimeSpan TemplatesCacheTtl = TimeSpan.FromHours(12);

    public sealed class Query : IRequest<BaseResult<ListWalletTemplatesResponseDto>>
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Search { get; set; }
        public long? CategoryId { get; set; }
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
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICacheService _cache;

        public Handler(
            IUnitOfWork unitOfWork,
            ICacheService cache)
        {
            _unitOfWork = unitOfWork;
            _cache = cache;
        }

        public async Task<BaseResult<ListWalletTemplatesResponseDto>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            try
            {
                var page = request.Page < 1 ? 1 : request.Page;
                var pageSize = request.PageSize < 1 ? 20 : request.PageSize;
                if (pageSize > 100) pageSize = 100;

                // Load the full active template set — cache-backed
                var allTemplates = await _cache.GetOrSetFastAsync(
                    CacheKeys.WalletTemplates(),
                    LoadAllTemplatesFromDbAsync,
                    timeout: TemplatesCacheTtl,
                    cancellationToken: cancellationToken)
                    ?? new List<WalletTemplateDto>();

                // ─── Filter in memory ────────────────────────
                IEnumerable<WalletTemplateDto> filtered = allTemplates;

                if (request.CategoryId.HasValue && request.CategoryId.Value > 0)
                {
                    filtered = filtered.Where(t => t.CategoryId == request.CategoryId.Value);
                }

                if (!string.IsNullOrWhiteSpace(request.Search))
                {
                    var term = request.Search.Trim().ToLowerInvariant();

                    filtered = filtered.Where(t =>
                        t.Name.ToLowerInvariant().Contains(term) ||
                        t.Description.ToLowerInvariant().Contains(term) ||
                        t.Tags.Any(tag => tag.ToLowerInvariant().Contains(term)));
                }

                // ─── Order + paginate in memory ──────────────
                var orderedList = filtered
                    .OrderBy(t => t.SortOrder)
                    .ThenBy(t => t.Id)
                    .ToList();

                var totalCount = orderedList.Count;

                var items = orderedList
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var totalPages = totalCount == 0
                    ? 0
                    : (int)Math.Ceiling(totalCount / (double)pageSize);

                var response = new ListWalletTemplatesResponseDto
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    Items = items,
                };

                return new BaseResult<ListWalletTemplatesResponseDto>(
                    HttpStatusCode.OK,
                    "Wallet templates retrieved successfully.",
                    response);
            }
            catch (Exception)
            {
                return new BaseResult<ListWalletTemplatesResponseDto>(
                    HttpStatusCode.InternalServerError,
                    "An error occurred while retrieving wallet templates. Please try again later.");
            }
        }

        private async Task<List<WalletTemplateDto>> LoadAllTemplatesFromDbAsync(
            CancellationToken cancellationToken)
        {
            var templates = await _unitOfWork.Query<WalletTemplate>()
                .AsNoTracking()
                .Where(t => t.IsActive)
                .Include(t => t.Category)
                .OrderBy(t => t.SortOrder)
                .ThenBy(t => t.Id)
                .ToListAsync(cancellationToken);

            return templates.Select(t => new WalletTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                CategoryId = t.CategoryId,
                CategoryName = t.Category?.Name ?? "Other",
                CategoryIcon = t.Category?.Icon ?? "FileText",
                DefaultTargetAmount = t.DefaultTargetAmount.ToDecimal(),
                DefaultReleaseAmount = t.DefaultReleaseAmount.ToDecimal(),
                DefaultFrequency = t.DefaultFrequency.ToString(),
                DefaultFrequencyConfig = t.DefaultFrequencyConfig,
                DefaultPayoutDestination = t.DefaultPayoutDestination
                    .ToString()
                    .ToLowerInvariant(),
                IconName = t.IconName,
                Tags = t.Tags.ToList(),
                SortOrder = t.SortOrder,
                UsageCount = t.UsageCount,
            }).ToList();
        }
    }
}